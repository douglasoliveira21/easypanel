using System.Text;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.IntegrationTests.Identity;

/// <summary>
/// Testes do <see cref="TokenService"/> (Task 3.2 / R2.1, R2.4, R2.5, R2.6),
/// exercitando emissão de JWT, emissão/rotação de refresh token e revogação em
/// massa sobre o provider relacional SQLite in-memory (reutiliza o
/// <see cref="AppDbContext"/> real, incluindo a configuração de
/// <see cref="RefreshToken"/> e seus índices).
/// </summary>
public sealed class TokenServiceTests : IDisposable
{
    private const string SigningKey = "token-service-tests-signing-key-not-a-secret-32b+";

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "easypanel-tests",
        Audience = "easypanel-tests",
        SigningKey = SigningKey,
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14,
    };

    public TokenServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Super Admin evita o filtro de tenant ao materializar entidades de teste.
        _tenantContext.SetSuperAdmin();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    // ---- JWT (R2.6) --------------------------------------------------------

    [Fact]
    public async Task CreateAccessToken_EmbedsExpectedClaims()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = TenantA };
        using var context = CreateContext();
        var service = CreateService(context);

        var jwt = service.CreateAccessToken(user, new[] { "Administrador", "Financeiro" }, Array.Empty<string>());

        var token = await ParseAndValidateAsync(jwt);

        Assert.Equal(user.Id.ToString(), token.GetClaim(JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(TenantA.ToString(), token.GetClaim(TokenService.TenantIdClaimType).Value);
        Assert.False(string.IsNullOrWhiteSpace(token.GetClaim(JwtRegisteredClaimNames.Jti).Value));
        Assert.Equal("0", token.GetClaim(TokenService.PermVersionClaimType).Value);

        var roles = token.GetPayloadValue<string[]>(TokenService.RolesClaimType);
        Assert.Equal(new[] { "Administrador", "Financeiro" }, roles);
    }

    [Fact]
    public async Task CreateAccessToken_ExpiresWithinFifteenMinutes()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = TenantA };
        using var context = CreateContext();
        var service = CreateService(context);

        var before = DateTimeOffset.UtcNow;
        var jwt = service.CreateAccessToken(user, Array.Empty<string>(), Array.Empty<string>());
        var token = await ParseAndValidateAsync(jwt);

        // Expiração ≤ 15 min após a emissão (R2.6), com folga para latência.
        var maxExpiry = before.AddMinutes(15).AddSeconds(5);
        Assert.True(token.ValidTo <= maxExpiry.UtcDateTime, $"ValidTo {token.ValidTo:o} > {maxExpiry:o}");
        Assert.True(token.ValidTo > before.UtcDateTime);
    }

    [Fact]
    public async Task CreateAccessToken_ForSuperAdminWithoutTenant_OmitsTenantClaim()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = null };
        using var context = CreateContext();
        var service = CreateService(context);

        var jwt = service.CreateAccessToken(user, new[] { "Super Admin" }, Array.Empty<string>());
        var token = await ParseAndValidateAsync(jwt);

        // GetClaim lança quando ausente; usamos TryGetClaim para asserir ausência.
        Assert.False(token.TryGetClaim(TokenService.TenantIdClaimType, out _));
    }

    // ---- Claim de Cliente (Fase 10 — Portal do Cliente, R2.1) -------------

    [Fact]
    public async Task CreateAccessToken_ForClienteUserWithCustomerId_EmbedsCustomerClaim()
    {
        var customerId = Guid.NewGuid();
        var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerId };
        using var context = CreateContext();
        var service = CreateService(context);

        var jwt = service.CreateAccessToken(user, new[] { "Cliente" }, Array.Empty<string>());
        var token = await ParseAndValidateAsync(jwt);

        Assert.Equal(customerId.ToString(), token.GetClaim(TokenService.CustomerIdClaimType).Value);
    }

    [Fact]
    public async Task CreateAccessToken_ForUserWithoutCustomerId_OmitsCustomerClaim()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = null };
        using var context = CreateContext();
        var service = CreateService(context);

        var jwt = service.CreateAccessToken(user, new[] { "Administrador" }, Array.Empty<string>());
        var token = await ParseAndValidateAsync(jwt);

        Assert.False(token.TryGetClaim(TokenService.CustomerIdClaimType, out _));
    }

    // ---- Refresh token issue (R2.1) ---------------------------------------

    [Fact]
    public async Task IssueRefreshToken_ReturnsRawTokenButStoresOnlyHash()
    {
        var userId = SeedUser();
        using var context = CreateContext();
        var service = CreateService(context);

        var issued = await service.IssueRefreshTokenAsync(userId, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(issued.RawToken));
        // O valor bruto nunca é o que fica armazenado; guarda-se apenas o hash.
        Assert.NotEqual(issued.RawToken, issued.Token.TokenHash);

        var stored = await context.Set<RefreshToken>().SingleAsync(t => t.Id == issued.Token.Id);
        Assert.NotEqual(issued.RawToken, stored.TokenHash);
        Assert.Equal(userId, stored.UserId);
        Assert.Null(stored.RevokedAt);
    }

    // ---- ValidateAndRotate (R2.4, R2.5) -----------------------------------

    [Fact]
    public async Task ValidateAndRotate_WithValidToken_RevokesOldAndIssuesNew()
    {
        var userId = SeedUser();
        using var context = CreateContext();
        var service = CreateService(context);

        var issued = await service.IssueRefreshTokenAsync(userId, CancellationToken.None);
        var originalId = issued.Token.Id;

        var result = await service.ValidateAndRotateAsync(issued.RawToken, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rotated = result.Value;

        // Novo token é diferente e ativo.
        Assert.NotEqual(issued.RawToken, rotated.RawToken);
        Assert.NotEqual(originalId, rotated.Token.Id);
        Assert.Null(rotated.Token.RevokedAt);

        // Token antigo foi revogado e aponta para o sucessor (R2.4).
        var old = await context.Set<RefreshToken>().SingleAsync(t => t.Id == originalId);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal(rotated.Token.TokenHash, old.ReplacedByTokenHash);
    }

    [Fact]
    public async Task ValidateAndRotate_WithUnknownToken_Fails()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.ValidateAndRotateAsync("not-a-real-token", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ValidateAndRotate_WithRevokedToken_Fails()
    {
        var userId = SeedUser();
        using var context = CreateContext();
        var service = CreateService(context);

        var issued = await service.IssueRefreshTokenAsync(userId, CancellationToken.None);

        // Primeira rotação revoga o token original.
        var first = await service.ValidateAndRotateAsync(issued.RawToken, CancellationToken.None);
        Assert.True(first.IsSuccess);

        // Reutilizar o token original (já revogado) deve falhar (R2.5).
        var reuse = await service.ValidateAndRotateAsync(issued.RawToken, CancellationToken.None);
        Assert.True(reuse.IsFailure);
    }

    [Fact]
    public async Task ValidateAndRotate_WithExpiredToken_Fails()
    {
        var userId = SeedUser();
        using var context = CreateContext();
        var service = CreateService(context);

        var issued = await service.IssueRefreshTokenAsync(userId, CancellationToken.None);

        // Força a expiração do token diretamente no armazenamento.
        var stored = await context.Set<RefreshToken>().SingleAsync(t => t.Id == issued.Token.Id);
        stored.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();

        var result = await service.ValidateAndRotateAsync(issued.RawToken, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ---- RevokeAllForUser --------------------------------------------------

    [Fact]
    public async Task RevokeAllForUser_RevokesOnlyThatUsersActiveTokens()
    {
        var user = SeedUser();
        var other = SeedUser();
        using var context = CreateContext();
        var service = CreateService(context);

        var t1 = await service.IssueRefreshTokenAsync(user, CancellationToken.None);
        var t2 = await service.IssueRefreshTokenAsync(user, CancellationToken.None);
        var otherToken = await service.IssueRefreshTokenAsync(other, CancellationToken.None);

        await service.RevokeAllForUserAsync(user, CancellationToken.None);

        var stored1 = await context.Set<RefreshToken>().SingleAsync(t => t.Id == t1.Token.Id);
        var stored2 = await context.Set<RefreshToken>().SingleAsync(t => t.Id == t2.Token.Id);
        var storedOther = await context.Set<RefreshToken>().SingleAsync(t => t.Id == otherToken.Token.Id);

        Assert.NotNull(stored1.RevokedAt);
        Assert.NotNull(stored2.RevokedAt);
        // O usuário não afetado mantém seu token ativo.
        Assert.Null(storedOther.RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForUser_ThenValidateRotate_Fails()
    {
        var user = SeedUser();
        using var context = CreateContext();
        var service = CreateService(context);

        var issued = await service.IssueRefreshTokenAsync(user, CancellationToken.None);
        await service.RevokeAllForUserAsync(user, CancellationToken.None);

        var result = await service.ValidateAndRotateAsync(issued.RawToken, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ---- Helpers -----------------------------------------------------------

    /// <summary>
    /// Persiste um <see cref="ApplicationUser"/> mínimo e retorna seu Id. O
    /// refresh token tem FK para o usuário; os testes de persistência exigem o
    /// usuário previamente gravado.
    /// </summary>
    private Guid SeedUser()
    {
        var id = Guid.NewGuid();
        using var context = CreateContext();
        context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"user-{id:N}",
            NormalizedUserName = $"USER-{id:N}",
            Email = $"user-{id:N}@example.com",
            NormalizedEmail = $"USER-{id:N}@EXAMPLE.COM",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        return id;
    }

    private TokenService CreateService(AppDbContext context) =>
        new(context, Options.Create(_jwtOptions));

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options, _tenantContext);
    }

    private async Task<JsonWebToken> ParseAndValidateAsync(string jwt)
    {
        var handler = new JsonWebTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(5),
        };

        var result = await handler.ValidateTokenAsync(jwt, parameters);
        Assert.True(result.IsValid, result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    /// <summary>Fake controlável de <see cref="ITenantContext"/> para os testes.</summary>
    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }

        public bool IsSuperAdmin { get; private set; }

        public bool HasTenant => TenantId.HasValue;

        public void SetTenant(Guid tenantId)
        {
            TenantId = tenantId;
            IsSuperAdmin = false;
        }

        public void SetSuperAdmin(Guid? tenantId = null)
        {
            TenantId = tenantId;
            IsSuperAdmin = true;
        }
    }
}
