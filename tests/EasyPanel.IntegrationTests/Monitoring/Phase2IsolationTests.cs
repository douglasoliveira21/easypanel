using System.Collections.Concurrent;
using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Suíte de isolamento multi-tenant e integridade da Fase 2 (Task 9.2 — R4.6, R9.4,
/// R11.5, R13, R17.2): um tenant não enxerga contadores/coletas de outro; a
/// idempotência de ingestão vale sob concorrência; o não-decréscimo de contador é
/// mantido.
/// </summary>
public sealed class Phase2IsolationTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("11110000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("22220000-0000-0000-0000-00000000000b");
    private static readonly Guid CustomerA = Guid.Parse("11110000-0000-0000-0000-0000000000c1");
    private static readonly Guid LocationA = Guid.Parse("11110000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerB = Guid.Parse("22220000-0000-0000-0000-0000000000c2");
    private static readonly Guid LocationB = Guid.Parse("22220000-0000-0000-0000-000000000002");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();

    public Phase2IsolationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedTenant(TenantA, CustomerA, LocationA);
        SeedTenant(TenantB, CustomerB, LocationB);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Counters_AreIsolatedByTenant()
    {
        // Impressora e leitura no Tenant A.
        _tenantContext.SetTenant(TenantA);
        var printerA = SeedPrinter(TenantA, CustomerA, LocationA);
        var counterA = new CounterService(CreateContext(), FakeUser(), new NoopAudit());
        await counterA.RecordAsync(printerA, CounterType.BlackAndWhite, null, 100, CounterSource.Api, null, null, CancellationToken.None);

        // Do Tenant B, a impressora de A é inexistente (404) e seu histórico é inacessível.
        _tenantContext.SetTenant(TenantB);
        var counterB = new CounterService(CreateContext(), FakeUser(), new NoopAudit());
        var list = await counterB.ListAsync(printerA, null, null, 10, CancellationToken.None);

        Assert.True(list.IsFailure);
        Assert.Equal(MonitoringErrors.NotFound.Code, list.Error.Code);
    }

    [Fact]
    public async Task Ingestion_Concurrent_SameKey_DoesNotDuplicate()
    {
        _tenantContext.SetTenant(TenantA);
        var clientId = SeedClient(TenantA, CustomerA, LocationA);

        // Dispara várias submissões concorrentes com a mesma IdempotencyKey.
        var acks = new ConcurrentBag<Guid>();
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            var service = new IngestionService(CreateContext(), ClientCtx(clientId));
            var ack = await service.SubmitAsync(Submission("concurrent-key"), CancellationToken.None);
            if (ack.IsSuccess)
            {
                acks.Add(ack.Value.CollectionId);
            }
        });

        await Task.WhenAll(tasks);

        using var verify = CreateContext();
        var count = await verify.Set<Collection>().IgnoreQueryFilters()
            .CountAsync(c => c.IdempotencyKey == "concurrent-key");

        // Exatamente uma coleta persistida, apesar da concorrência (R13).
        Assert.Equal(1, count);
        // Todos os acks apontam para a mesma coleta.
        Assert.Single(acks.Distinct());
    }

    [Fact]
    public async Task Counter_NonDecrease_IsEnforced()
    {
        _tenantContext.SetTenant(TenantA);
        var printer = SeedPrinter(TenantA, CustomerA, LocationA);
        var service = new CounterService(CreateContext(), FakeUser(), new NoopAudit());

        await service.RecordAsync(printer, CounterType.BlackAndWhite, null, 900, CounterSource.Api, null, null, CancellationToken.None);
        var lower = await service.RecordAsync(printer, CounterType.BlackAndWhite, null, 10, CounterSource.Api, null, null, CancellationToken.None);

        Assert.True(lower.IsFailure);
        Assert.Equal(MonitoringErrors.CounterDecrease.Code, lower.Error.Code);
    }

    private ClientCollectionSubmission Submission(string key) =>
        new(key, null, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, true, null,
            new[] { new SubmittedCounter("BlackAndWhite", null, 5) }, "online");

    private Guid SeedPrinter(Guid tenant, Guid customer, Guid location)
    {
        var previousTenant = _tenantContext.TenantId;
        _tenantContext.SetSuperAdmin();
        using var context = CreateContext();
        var id = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = id,
            TenantId = tenant,
            CustomerId = customer,
            LocationId = location,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        if (previousTenant is { } t)
        {
            _tenantContext.SetTenant(t);
        }

        return id;
    }

    private Guid SeedClient(Guid tenant, Guid customer, Guid location)
    {
        var previousTenant = _tenantContext.TenantId;
        _tenantContext.SetSuperAdmin();
        using var context = CreateContext();
        var id = Guid.NewGuid();
        context.Set<WindowsClient>().Add(new WindowsClient
        {
            Id = id,
            TenantId = tenant,
            CustomerId = customer,
            LocationId = location,
            UniqueId = $"agent-{id:N}",
            Hostname = "host",
            AgentVersion = "1.0.0",
            State = WindowsClientState.Active,
            SecretHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        if (previousTenant is { } t)
        {
            _tenantContext.SetTenant(t);
        }

        return id;
    }

    private void SeedTenant(Guid tenant, Guid customer, Guid location)
    {
        using var context = CreateContext();
        context.Set<Customer>().Add(new Customer
        {
            Id = customer,
            TenantId = tenant,
            RazaoSocial = $"Cliente {tenant:N}",
            Cnpj = tenant == TenantA ? "11222333000181" : "04252011000110",
            Status = CustomerStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Location>().Add(new Location
        {
            Id = location,
            TenantId = tenant,
            CustomerId = customer,
            Nome = "Local",
            Status = LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options, _tenantContext);
    }

    private static ICurrentUserAccessor FakeUser() => new FakeCurrentUser();

    private static IClientContext ClientCtx(Guid clientId) => new FakeClientContext(clientId, TenantA, LocationA);

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId => Guid.Parse("99999999-9999-9999-9999-999999999999");
    }

    private sealed class NoopAudit : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
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
