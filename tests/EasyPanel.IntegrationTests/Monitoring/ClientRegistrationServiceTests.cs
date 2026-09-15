using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="ClientRegistrationService"/> (Task 3.1 — R3.2–R3.7):
/// registro válido cria o agente vinculado a tenant/local e emite credenciais;
/// múltiplos agentes por local; chave inválida/expirada → 401; auditoria do
/// registro.
/// </summary>
public sealed class ClientRegistrationServiceTests : IDisposable
{
    private const string SigningKey = "client-register-tests-signing-key-not-secret-32+";

    private static readonly Guid TenantA = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid CustomerA = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid LocationA = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly SqliteConnection _connection;
    private readonly SuperAdminContext _tenantContext = new();
    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "easypanel-tests",
        Audience = "easypanel-tests",
        SigningKey = SigningKey,
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14,
    };

    public ClientRegistrationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task RegisterAsync_ValidKey_CreatesClientAndIssuesCredentials()
    {
        var rawKey = await SeedProvisioningKeyAsync();
        var (service, audit) = CreateService();

        var result = await service.RegisterAsync(
            new RegisterClientRequest(rawKey, "AGENT-001", "host-1", "1.0.0"),
            "10.0.0.1",
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.ClientId);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ClientSecret));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Tokens.AccessToken));

        using var context = CreateContext();
        var client = await context.Set<WindowsClient>().IgnoreQueryFilters()
            .SingleAsync(c => c.Id == result.Value.ClientId);
        Assert.Equal(TenantA, client.TenantId);
        Assert.Equal(LocationA, client.LocationId);

        Assert.Contains(audit.Entries, e => e.Action == "client.register" && e.Result == AuditResult.Success);
    }

    [Fact]
    public async Task RegisterAsync_MultipleAgents_SameLocation_Allowed()
    {
        var rawKey = await SeedProvisioningKeyAsync();
        var (service, _) = CreateService();

        var first = await service.RegisterAsync(
            new RegisterClientRequest(rawKey, "AGENT-A", "host-a", "1.0.0"), null, CancellationToken.None);
        var second = await service.RegisterAsync(
            new RegisterClientRequest(rawKey, "AGENT-B", "host-b", "1.0.0"), null, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.ClientId, second.Value.ClientId);
    }

    [Fact]
    public async Task RegisterAsync_InvalidKey_Fails401AndAudits()
    {
        await SeedProvisioningKeyAsync();
        var (service, audit) = CreateService();

        var result = await service.RegisterAsync(
            new RegisterClientRequest("chave-invalida", "AGENT-X", "host-x", "1.0.0"),
            "10.0.0.9",
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MonitoringErrors.Unauthorized.Code, result.Error.Code);
        Assert.Contains(audit.Entries, e => e.Action == "client.register" && e.Result == AuditResult.Failure);
    }

    [Fact]
    public async Task RegisterAsync_ExpiredKey_Fails()
    {
        var rawKey = await SeedProvisioningKeyAsync(expired: true);
        var (service, _) = CreateService();

        var result = await service.RegisterAsync(
            new RegisterClientRequest(rawKey, "AGENT-Y", "host-y", "1.0.0"), null, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private (ClientRegistrationService Service, FakeAuditLogger Audit) CreateService()
    {
        var factory = new TestSystemDbContextFactory(_connection);
        var tokenService = new ClientTokenService(CreateContext(), Options.Create(_jwtOptions));
        var audit = new FakeAuditLogger();
        return (new ClientRegistrationService(factory, tokenService, audit), audit);
    }

    private async Task<string> SeedProvisioningKeyAsync(bool expired = false)
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

        var rawKey = ClientSecretHasher.GenerateSecret();
        context.Set<LocationProvisioningKey>().Add(new LocationProvisioningKey
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            KeyHash = ClientSecretHasher.Hash(rawKey),
            ExpiresAt = expired ? DateTimeOffset.UtcNow.AddMinutes(-5) : DateTimeOffset.UtcNow.AddDays(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return rawKey;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new AppDbContext(options, _tenantContext);
    }

    /// <summary>Fábrica de contexto de sistema apoiada na conexão SQLite de teste.</summary>
    private sealed class TestSystemDbContextFactory(SqliteConnection connection) : ISystemDbContextFactory
    {
        public AppDbContext Create()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            return new AppDbContext(options, SystemTenantContext.Instance);
        }
    }

    /// <summary>Auditor em memória para inspeção nos testes.</summary>
    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Contexto de tenant Super Admin para materializar dados de teste.</summary>
    private sealed class SuperAdminContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSuperAdmin => true;

        public bool HasTenant => false;
    }
}
