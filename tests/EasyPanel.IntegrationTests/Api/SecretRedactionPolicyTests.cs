using EasyPanel.Api.Observability;
using Serilog.Core;
using Serilog.Events;

namespace EasyPanel.IntegrationTests.Api;

/// <summary>
/// Unit tests da <see cref="SecretRedactionPolicy"/> (Task 9.3 / R11.2): garante
/// que propriedades cujo nome sugira dado sensível (senha, token, segredo, hash,
/// stamp de segurança, etc.) sejam mascaradas antes da serialização do log, e que
/// campos não sensíveis sejam preservados.
/// </summary>
public class SecretRedactionPolicyTests
{
    private const string Redacted = "***REDACTED***";

    [Fact]
    public void Destructure_MasksSensitivePropertiesAndKeepsOthers()
    {
        var value = new SampleLogModel
        {
            Email = "user@example.com",
            Password = "SuperSecret123!",
            AccessToken = "eyJhbGciOi...",
            PasswordHash = "AQAAAA...",
            SecurityStamp = "ABCDEF",
            ApiKey = "sk-live-xyz",
            TenantId = "acme",
        };

        var structure = Destructure(value);
        var props = structure.Properties.ToDictionary(p => p.Name, p => Scalar(p.Value));

        // Sensíveis mascarados (R11.2).
        Assert.Equal(Redacted, props["Password"]);
        Assert.Equal(Redacted, props["AccessToken"]);
        Assert.Equal(Redacted, props["PasswordHash"]);
        Assert.Equal(Redacted, props["SecurityStamp"]);
        Assert.Equal(Redacted, props["ApiKey"]);

        // Não sensíveis preservados.
        Assert.Equal("user@example.com", props["Email"]);
        Assert.Equal("acme", props["TenantId"]);
    }

    [Fact]
    public void Destructure_DoesNotHandlePrimitivesOrStrings()
    {
        var policy = new SecretRedactionPolicy();
        var factory = new NoopValueFactory();

        Assert.False(policy.TryDestructure("a plain string", factory, out _));
        Assert.False(policy.TryDestructure(42, factory, out _));
    }

    private static StructureValue Destructure(object value)
    {
        var policy = new SecretRedactionPolicy();
        var handled = policy.TryDestructure(value, new NoopValueFactory(), out var result);
        Assert.True(handled);
        return Assert.IsType<StructureValue>(result);
    }

    private static string? Scalar(LogEventPropertyValue value) =>
        value is ScalarValue scalar ? scalar.Value?.ToString() : value.ToString();

    private sealed class SampleLogModel
    {
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string AccessToken { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
        public string SecurityStamp { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
    }

    /// <summary>Fábrica de valores que cria escalares simples, suficiente para o teste.</summary>
    private sealed class NoopValueFactory : ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects) =>
            new ScalarValue(value);
    }
}
