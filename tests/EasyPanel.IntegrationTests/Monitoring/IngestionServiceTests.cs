using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="IngestionService"/> (Task 4.1 — R13.1–R13.4, R14.1):
/// submissão nova enfileira (persiste coleta pendente); reenvio idempotente não
/// duplica e retorna ack equivalente.
/// </summary>
public sealed class IngestionServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("45454545-4545-4545-4545-454545454545");
    private static readonly Guid CustomerA = Guid.Parse("56565656-5656-5656-5656-565656565656");
    private static readonly Guid LocationA = Guid.Parse("67676767-6767-6767-6767-676767676767");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private Guid _clientId;

    public IngestionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        _clientId = SeedClient();

        // A partir daqui, o contexto opera como o tenant do agente (como em
        // produção, onde o handler de autenticação do cliente resolve o tenant).
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Submit_NewSubmission_PersistsCollection()
    {
        using var context = CreateContext();
        var service = new IngestionService(context, ClientContextFor(_clientId));

        var ack = await service.SubmitAsync(NewSubmission("key-1"), CancellationToken.None);

        Assert.True(ack.IsSuccess);
        Assert.False(ack.Value.Duplicate);

        using var verify = CreateContext();
        Assert.True(await verify.Set<Collection>().IgnoreQueryFilters()
            .AnyAsync(c => c.Id == ack.Value.CollectionId && c.IdempotencyKey == "key-1"));
    }

    [Fact]
    public async Task Submit_Duplicate_ReturnsEquivalentAckWithoutDuplicating()
    {
        using var context = CreateContext();
        var service = new IngestionService(context, ClientContextFor(_clientId));

        var first = await service.SubmitAsync(NewSubmission("key-dup"), CancellationToken.None);

        using var context2 = CreateContext();
        var service2 = new IngestionService(context2, ClientContextFor(_clientId));
        var second = await service2.SubmitAsync(NewSubmission("key-dup"), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.Duplicate);
        Assert.Equal(first.Value.CollectionId, second.Value.CollectionId);

        using var verify = CreateContext();
        var count = await verify.Set<Collection>().IgnoreQueryFilters()
            .CountAsync(c => c.IdempotencyKey == "key-dup");
        Assert.Equal(1, count);
    }

    private ClientCollectionSubmission NewSubmission(string key) =>
        new(
            key,
            PrinterId: null,
            StartedAt: DateTimeOffset.UtcNow.AddSeconds(-5),
            FinishedAt: DateTimeOffset.UtcNow,
            Success: true,
            Errors: null,
            Counters: new[] { new SubmittedCounter("BlackAndWhite", null, 1000) },
            RawStatus: "online");

    private Guid SeedClient()
    {
        using var context = CreateContext();
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
            State = WindowsClientState.Active,
            SecretHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        return id;
    }

    private static IClientContext ClientContextFor(Guid clientId) =>
        new FakeClientContext(clientId, TenantA, LocationA);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FakeClientContext(Guid clientId, Guid tenantId, Guid locationId) : IClientContext
    {
        public Guid? ClientId => clientId;

        public Guid? TenantId => tenantId;

        public Guid? LocationId => locationId;

        public bool IsAuthenticated => true;
    }

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
