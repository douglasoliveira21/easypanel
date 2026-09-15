using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="ClientAuthService"/> (Task 2.2 — R4.1, R4.4, R4.5):
/// autenticação por client_id + secret com hash verificado em tempo constante,
/// emissão/rotação de tokens e rejeição de credenciais inválidas/expiradas (→ 401).
/// </summary>
public sealed class ClientAuthServiceTests : IDisposable
{
    private const string SigningKey = "client-auth-tests-signing-key-not-a-secret-1234+";

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CustomerA = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LocationA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

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

    public ClientAuthServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _tenantContext.SetSuperAdmin();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_IssuesTokens()
    {
        var (clientId, secret) = await SeedClientAsync();
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.AuthenticateAsync(clientId.ToString(), secret, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefreshToken));
    }

    [Fact]
    public async Task AuthenticateAsync_WrongSecret_Fails()
    {
        var (clientId, _) = await SeedClientAsync();
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.AuthenticateAsync(clientId.ToString(), "segredo-errado", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MonitoringErrors.Unauthorized.Code, result.Error.Code);
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownClient_Fails()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.AuthenticateAsync(Guid.NewGuid().ToString(), "qualquer", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task AuthenticateAsync_DisabledClient_Fails()
    {
        var (clientId, secret) = await SeedClientAsync(WindowsClientState.Disabled);
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.AuthenticateAsync(clientId.ToString(), secret, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task RefreshAsync_ThenAuthenticate_Rotates()
    {
        var (clientId, secret) = await SeedClientAsync();
        using var context = CreateContext();
        var service = CreateService(context);

        var auth = await service.AuthenticateAsync(clientId.ToString(), secret, CancellationToken.None);
        var refreshed = await service.RefreshAsync(auth.Value.RefreshToken, CancellationToken.None);

        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(auth.Value.RefreshToken, refreshed.Value.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_Fails()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.RefreshAsync("token-invalido", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private ClientAuthService CreateService(AppDbContext context) =>
        new(context, new ClientTokenService(context, Options.Create(_jwtOptions)));

    private async Task<(Guid ClientId, string Secret)> SeedClientAsync(
        WindowsClientState state = WindowsClientState.Active)
    {
        using var context = CreateContext();

        if (!await context.Set<Customer>().IgnoreQueryFilters().AnyAsync(c => c.Id == CustomerA))
        {
            context.Set<Customer>().Add(new Customer
            {
                Id = CustomerA,
                TenantId = TenantA,
                RazaoSocial = "Cliente A",
                Cnpj = "11222333000181",
                Status = CustomerStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            context.Set<Location>().Add(new Location
            {
                Id = LocationA,
                TenantId = TenantA,
                CustomerId = CustomerA,
                Nome = "Local A",
                Status = LocationStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        var id = Guid.NewGuid();
        var secret = ClientSecretHasher.GenerateSecret();
        context.Set<WindowsClient>().Add(new WindowsClient
        {
            Id = id,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            UniqueId = $"agent-{id:N}",
            Hostname = "host-a",
            AgentVersion = "1.0.0",
            State = state,
            SecretHash = ClientSecretHasher.Hash(secret),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return (id, secret);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options, _tenantContext);
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
