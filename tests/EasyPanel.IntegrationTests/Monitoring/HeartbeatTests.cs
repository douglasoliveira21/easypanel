using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes de heartbeat (Task 3.2 — R6.1–R6.5): o heartbeat atualiza estado/horário;
/// a ausência além do limite gera <see cref="PrinterEvent"/> e marca
/// <see cref="WindowsClientState.HeartbeatMissing"/>; o retorno restaura
/// <see cref="WindowsClientState.Active"/>.
/// </summary>
public sealed class HeartbeatTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid CustomerA = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid LocationA = Guid.Parse("34343434-3434-3434-3434-343434343434");

    private readonly SqliteConnection _connection;
    private readonly SuperAdminContext _tenantContext = new();

    public HeartbeatTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Heartbeat_UpdatesStateAndTimestamp()
    {
        var clientId = await SeedClientAsync(WindowsClientState.Registered);
        using var context = CreateContext();
        var service = new HeartbeatService(context, new FakeClientContext(clientId, TenantA, LocationA));

        var result = await service.RecordAsync(
            new HeartbeatRequest("1.2.0", "host-new", DateTimeOffset.UtcNow, "Active", 3, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var updated = await ReadClientAsync(clientId);
        Assert.Equal(WindowsClientState.Active, updated.State);
        Assert.Equal("1.2.0", updated.AgentVersion);
        Assert.NotNull(updated.LastHeartbeatAt);
    }

    [Fact]
    public async Task Monitor_MarksMissing_AndGeneratesEvent()
    {
        var clientId = await SeedClientAsync(
            WindowsClientState.Active,
            lastHeartbeat: DateTimeOffset.UtcNow.AddMinutes(-30));

        var monitor = CreateMonitor(now: DateTimeOffset.UtcNow);
        var count = await monitor.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(1, count);
        var updated = await ReadClientAsync(clientId);
        Assert.Equal(WindowsClientState.HeartbeatMissing, updated.State);

        using var context = CreateContext();
        var generatedEvent = await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .SingleAsync(e => e.WindowsClientId == clientId && e.Type == PrinterEventType.HeartbeatMissing);

        // CreatedAtTicks (Fase 3 — base do cursor do Motor_de_Alertas) deve ser
        // consistente com CreatedAt, para toda gravação de PrinterEvent.
        Assert.Equal(generatedEvent.CreatedAt.UtcTicks, generatedEvent.CreatedAtTicks);
    }

    [Fact]
    public async Task Monitor_DoesNotMark_RecentHeartbeat()
    {
        await SeedClientAsync(WindowsClientState.Active, lastHeartbeat: DateTimeOffset.UtcNow.AddSeconds(-10));

        var monitor = CreateMonitor(now: DateTimeOffset.UtcNow);
        var count = await monitor.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Heartbeat_AfterMissing_RestoresActive()
    {
        var clientId = await SeedClientAsync(
            WindowsClientState.HeartbeatMissing,
            lastHeartbeat: DateTimeOffset.UtcNow.AddMinutes(-30));

        using var context = CreateContext();
        var service = new HeartbeatService(context, new FakeClientContext(clientId, TenantA, LocationA));

        await service.RecordAsync(
            new HeartbeatRequest("1.0.0", "host-a", DateTimeOffset.UtcNow, "Active", 1, null),
            CancellationToken.None);

        var updated = await ReadClientAsync(clientId);
        Assert.Equal(WindowsClientState.Active, updated.State);
    }

    private HeartbeatMonitor CreateMonitor(DateTimeOffset now)
    {
        var options = Options.Create(new MonitoringOptions
        {
            HeartbeatMissingThresholdSeconds = 300,
            HeartbeatScanIntervalSeconds = 60,
        });
        var factory = new TestSystemDbContextFactory(_connection);
        return new HeartbeatMonitor(factory, options, NullLogger<HeartbeatMonitor>.Instance, new FixedClock(now));
    }

    private async Task<WindowsClient> ReadClientAsync(Guid clientId)
    {
        using var context = CreateContext();
        return await context.Set<WindowsClient>().IgnoreQueryFilters().SingleAsync(c => c.Id == clientId);
    }

    private async Task<Guid> SeedClientAsync(
        WindowsClientState state,
        DateTimeOffset? lastHeartbeat = null)
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
            SecretHash = "hash",
            LastHeartbeatAt = lastHeartbeat,
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

    private sealed class FakeClientContext(Guid clientId, Guid tenantId, Guid locationId) : IClientContext
    {
        public Guid? ClientId => clientId;

        public Guid? TenantId => tenantId;

        public Guid? LocationId => locationId;

        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SuperAdminContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSuperAdmin => true;

        public bool HasTenant => false;
    }
}
