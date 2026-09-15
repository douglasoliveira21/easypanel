using EasyPanel.WindowsClient;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes do <see cref="SecretRedactor"/> (Task 8.2 — R5.6/R5.7): segredos são
/// mascarados em logs; conteúdo não sensível é preservado.
/// </summary>
public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("{\"client_secret\":\"abc123\"}")]
    [InlineData("token=xyz789")]
    [InlineData("community: public-secret")]
    [InlineData("password = hunter2")]
    public void Redact_MasksSensitiveValues(string input)
    {
        var result = SecretRedactor.Redact(input);

        Assert.Contains(SecretRedactor.Mask, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_MasksBearerToken()
    {
        var result = SecretRedactor.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.abc.def");

        Assert.Contains(SecretRedactor.Mask, result, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGci", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_PreservesNonSensitiveContent()
    {
        const string input = "Coleta concluída para host 10.0.0.5 com 3 impressoras.";

        var result = SecretRedactor.Redact(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsInput()
    {
        Assert.Equal(string.Empty, SecretRedactor.Redact(null));
        Assert.Equal(string.Empty, SecretRedactor.Redact(string.Empty));
    }
}
