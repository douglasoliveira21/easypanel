using System.Text;
using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="ClientTokenService"/> (Task 2.1/2.2 — R4.1, R4.2, R4.4,
/// R4.5) sobre SQLite in-memory, reutilizando o <see cref="AppDbContext"/> real
/// (inclui a configuração de <see cref="ClientRefreshToken"/> e o registro de
/// <see cref="WindowsClient"/>). Verifica a emissão do token de acesso com
/// audience/identidade corretos, a rotação do refresh e a rejeição de refresh
/// inválido.
/// </summary>
public sealed class ClientTokenServiceTests : IDisposable
{
    private const string SigningKey = "client-token-tests-signing-key-not-a-secret-32b+";

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LocationA = Guid.Parse("33333333-3333-3333-3333-333333333333");

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

    public ClientTokenServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _tenantContext.SetSuperAdmin();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task IssueAsync_EmbedsClientAudienceAndIdentity()
    {
        var clientId = await SeedClientAsync();
        using var context = CreateContext();
        var service = new ClientTokenService(context, Options.Create(_jwtOptions));

        var pair = await service.IssueAsync(clientId, TenantA, LocationA, CancellationToken.None);

        var token = await ParseAsync(pair.AccessToken);
        Assert.Equal(clientId.ToString(), token.GetClaim(ClientAuthConstants.ClientIdClaimType).Value);
        Assert.Equal(TenantA.ToString(), token.GetClaim(ClientAuthConstants.TenantIdClaimType).Value);
        Assert.Equal(LocationA.ToString(), token.GetClaim(ClientAuthConstants.LocationIdClaimType).Value);
        Assert.Equal(ClientAuthConstants.TokenKindValue, token.GetClaim(ClientAuthConstants.TokenKindClaimType).Value);
        Assert.Contains(ClientAuthConstants.Audience, token.Audiences);
        Assert.False(string.IsNullOrWhiteSpace(pair.RefreshToken));
    }

    [Fact]
    public async Task RefreshAsync_RotatesAndReturnsNewPair()
    {
        var clientId = await SeedClientAsync();
        using var context = CreateContext();
        var service = new ClientTokenService(context, Options.Create(_jwtOptions));

        var first = await service.IssueAsync(clientId, TenantA, LocationA, CancellationToken.None);
        var refreshed = await service.RefreshAsync(first.RefreshToken, CancellationToken.None);

        Assert.NotNull(refreshed);
        Assert.NotEqual(first.RefreshToken, refreshed!.RefreshToken);

        // O refresh antigo não pode mais ser usado (rotacionado/revogado).
        var reused = await service.RefreshAsync(first.RefreshToken, CancellationToken.None);
        Assert.Null(reused);
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ReturnsNull()
    {
        using var context = CreateContext();
        var service = new ClientTokenService(context, Options.Create(_jwtOptions));

        var result = await service.RefreshAsync("nao-existe", CancellationToken.None);

        Assert.Null(result);
    }

    private async Task<Guid> SeedClientAsync()
    {
        using var context = CreateContext();

        context.Set<EasyPanel.Modules.Customers.Customer>().Add(new EasyPanel.Modules.Customers.Customer
        {
            Id = CustomerA,
            TenantId = TenantA,
            RazaoSocial = "Cliente A",
            Cnpj = "11222333000181",
            Status = EasyPanel.Modules.Customers.CustomerStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<EasyPanel.Modules.Customers.Location>().Add(new EasyPanel.Modules.Customers.Location
        {
            Id = LocationA,
            TenantId = TenantA,
            CustomerId = CustomerA,
            Nome = "Local A",
            Status = EasyPanel.Modules.Customers.LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var id = Guid.NewGuid();
        context.Set<WindowsClient>().Add(new WindowsClient
        {
            Id = id,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            UniqueId = $"agent-{id:N}",
            Hostname = "host-a",
            AgentVersion = "1.0.0",
            State = WindowsClientState.Registered,
            SecretHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options, _tenantContext);
    }

    private async Task<JsonWebToken> ParseAsync(string jwt)
    {
        var handler = new JsonWebTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = ClientAuthConstants.Audience,
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
