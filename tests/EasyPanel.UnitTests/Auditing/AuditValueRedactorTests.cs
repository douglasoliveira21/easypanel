using System.Text.Json;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;

namespace EasyPanel.UnitTests.Auditing;

/// <summary>
/// Unit tests do <see cref="AuditValueRedactor"/> (Task 5.2 / R11.2): a
/// serialização de estados para a trilha de auditoria (<c>OldValues</c>/
/// <c>NewValues</c>) deve <b>omitir</b> qualquer campo sensível — hash de senha,
/// <c>SecurityStamp</c>, <c>ConcurrencyStamp</c> e quaisquer campos de token/segredo
/// — de modo que segredos nunca sejam persistidos.
/// </summary>
public sealed class AuditValueRedactorTests
{
    [Fact]
    public void Serialize_ApplicationUser_ExcludesSensitiveIdentityFields()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "user@tenant-a.example.com",
            Email = "user@tenant-a.example.com",
            TenantId = Guid.NewGuid(),
            IsActive = true,
            MfaEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
            PasswordHash = "PBKDF2$super-secret-hash",
            SecurityStamp = "SECURITY-STAMP-VALUE",
            ConcurrencyStamp = "CONCURRENCY-STAMP-VALUE",
        };

        var json = AuditValueRedactor.Serialize(user);

        Assert.NotNull(json);

        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;

        // Campos sensíveis do Identity NÃO aparecem (R11.2).
        Assert.False(root.TryGetProperty(nameof(ApplicationUser.PasswordHash), out _));
        Assert.False(root.TryGetProperty(nameof(ApplicationUser.SecurityStamp), out _));
        Assert.False(root.TryGetProperty(nameof(ApplicationUser.ConcurrencyStamp), out _));

        // E os valores em si jamais aparecem na string serializada.
        Assert.DoesNotContain("super-secret-hash", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECURITY-STAMP-VALUE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CONCURRENCY-STAMP-VALUE", json, StringComparison.Ordinal);

        // Campos não-sensíveis permanecem (a redaction não apaga a trilha útil).
        Assert.True(root.TryGetProperty(nameof(ApplicationUser.Email), out var email));
        Assert.Equal("user@tenant-a.example.com", email.GetString());
        Assert.True(root.TryGetProperty(nameof(ApplicationUser.IsActive), out _));
    }

    [Fact]
    public void Serialize_ExcludesTokenAndSecretNamedFields()
    {
        var payload = new SampleWithTokens
        {
            Name = "keep-me",
            RefreshToken = "opaque-refresh-token",
            AccessToken = "jwt-access-token",
            ResetToken = "reset-token-value",
            ApiSecret = "top-secret",
            Password = "PlainTextPassword",
        };

        var json = AuditValueRedactor.Serialize(payload);

        Assert.NotNull(json);
        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty(nameof(SampleWithTokens.Name), out var name));
        Assert.Equal("keep-me", name.GetString());

        Assert.False(root.TryGetProperty(nameof(SampleWithTokens.RefreshToken), out _));
        Assert.False(root.TryGetProperty(nameof(SampleWithTokens.AccessToken), out _));
        Assert.False(root.TryGetProperty(nameof(SampleWithTokens.ResetToken), out _));
        Assert.False(root.TryGetProperty(nameof(SampleWithTokens.ApiSecret), out _));
        Assert.False(root.TryGetProperty(nameof(SampleWithTokens.Password), out _));

        Assert.DoesNotContain("opaque-refresh-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("jwt-access-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("reset-token-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PlainTextPassword", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_Null_ReturnsNull()
    {
        Assert.Null(AuditValueRedactor.Serialize<ApplicationUser>(null));
    }

    [Theory]
    [InlineData("PasswordHash", true)]
    [InlineData("passwordhash", true)]
    [InlineData("SecurityStamp", true)]
    [InlineData("ConcurrencyStamp", true)]
    [InlineData("RefreshToken", true)]
    [InlineData("access_token", true)]
    [InlineData("ApiSecret", true)]
    [InlineData("Password", true)]
    [InlineData("Email", false)]
    [InlineData("IsActive", false)]
    [InlineData("TenantId", false)]
    public void IsSensitive_ClassifiesFieldsByPolicy(string propertyName, bool expected)
    {
        Assert.Equal(expected, AuditValueRedactor.IsSensitive(propertyName));
    }

    private sealed class SampleWithTokens
    {
        public string Name { get; set; } = string.Empty;

        public string RefreshToken { get; set; } = string.Empty;

        public string AccessToken { get; set; } = string.Empty;

        public string ResetToken { get; set; } = string.Empty;

        public string ApiSecret { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}
