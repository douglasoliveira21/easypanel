using EasyPanel.Infrastructure.Alerting;
using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Alerting;

/// <summary>
/// Testes do <see cref="AlertEngine"/> (Tasks 5.1-5.3 — R2, R8.5/R8.6): leitura
/// incremental idempotente, casamento de regra por tipo/escopo, limiar/janela,
/// dedupe de Alerta aberto, isolamento entre tenants e resolução automática.
/// </summary>
public sealed class AlertEngineTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("51515151-5151-5151-5151-515151515151");
    private static readonly Guid TenantB = Guid.Parse("62626262-6262-6262-6262-626262626262");
    private static readonly Guid CustomerA = Guid.Parse("73737373-7373-7373-7373-737373737373");
    private static readonly Guid LocationA = Guid.Parse("84848484-8484-8484-8484-848484848484");

    private readonly SqliteConnection _connection;
    private readonly SuperAdminContext _tenantContext = new();
    private Guid _printerId;
    private Guid _clientId;

    public AlertEngineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedTenantA();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ScanOnce_NoThreshold_CreatesAlert_OnFirstOccurrence()
    {
        var ruleId = await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: null, window: null);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        var processed = await engine.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        using var context = CreateContext();
        var alert = await context.Set<Alert>().IgnoreQueryFilters().SingleAsync(a => a.AlertRuleId == ruleId);
        Assert.Equal(AlertState.Open, alert.State);
        Assert.Equal(1, alert.OccurrenceCount);
    }

    [Fact]
    public async Task ScanOnce_ProcessedTwice_DoesNotDuplicateAlert()
    {
        await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: null, window: null);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        await engine.ScanOnceAsync(CancellationToken.None);
        var secondRun = await engine.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(0, secondRun);
        using var context = CreateContext();
        Assert.Equal(1, await context.Set<Alert>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ScanOnce_RepeatedOccurrences_AggregatesInsteadOfDuplicating()
    {
        await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: null, window: null);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        await engine.ScanOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        var alert = await context.Set<Alert>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(3, alert.OccurrenceCount);
    }

    [Fact]
    public async Task ScanOnce_WithThreshold_DoesNotTrigger_BeforeReachingCount()
    {
        await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: 3, window: 60);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        await engine.ScanOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        Assert.Equal(0, await context.Set<Alert>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ScanOnce_WithThreshold_Triggers_OnReachingCount()
    {
        await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: 3, window: 60);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        await engine.ScanOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        Assert.Equal(1, await context.Set<Alert>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ScanOnce_InactiveRule_NeverTriggers()
    {
        await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: null, window: null, isActive: false);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        await engine.ScanOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        Assert.Equal(0, await context.Set<Alert>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ScanOnce_RuleFromAnotherTenant_NeverConsidered()
    {
        await SeedRuleAsync(TenantB, [PrinterEventType.CollectionFailure], threshold: null, window: null);
        await SeedEventAsync(TenantA, PrinterEventType.CollectionFailure, printerId: _printerId);

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        await engine.ScanOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        Assert.Equal(0, await context.Set<Alert>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task AutoResolve_WhenPrinterBackOnline_ResolvesAlert()
    {
        var ruleId = await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: null, window: null, autoResolve: true);
        var alertId = await SeedOpenAlertAsync(TenantA, ruleId, printerId: _printerId);

        using (var context = CreateContext())
        {
            var printer = await context.Set<Printer>().IgnoreQueryFilters().SingleAsync(p => p.Id == _printerId);
            printer.Status = PrinterStatus.Online;
            await context.SaveChangesAsync();
        }

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        var resolvedCount = await engine.AutoResolveOnceAsync(CancellationToken.None);

        Assert.Equal(1, resolvedCount);
        using var verifyContext = CreateContext();
        var alert = await verifyContext.Set<Alert>().IgnoreQueryFilters().SingleAsync(a => a.Id == alertId);
        Assert.Equal(AlertState.Resolved, alert.State);
        Assert.True(alert.AutoResolved);
        Assert.Null(alert.ResolvedByUserId);
    }

    [Fact]
    public async Task AutoResolve_WhenAutoResolveDisabled_NeverCloses()
    {
        var ruleId = await SeedRuleAsync(TenantA, [PrinterEventType.CollectionFailure], threshold: null, window: null, autoResolve: false);
        var alertId = await SeedOpenAlertAsync(TenantA, ruleId, printerId: _printerId);

        using (var context = CreateContext())
        {
            var printer = await context.Set<Printer>().IgnoreQueryFilters().SingleAsync(p => p.Id == _printerId);
            printer.Status = PrinterStatus.Online;
            await context.SaveChangesAsync();
        }

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        var resolvedCount = await engine.AutoResolveOnceAsync(CancellationToken.None);

        Assert.Equal(0, resolvedCount);
        using var verifyContext = CreateContext();
        var alert = await verifyContext.Set<Alert>().IgnoreQueryFilters().SingleAsync(a => a.Id == alertId);
        Assert.Equal(AlertState.Open, alert.State);
    }

    [Fact]
    public async Task AutoResolve_WhenAgentBackActive_ResolvesHeartbeatAlert()
    {
        var ruleId = await SeedRuleAsync(TenantA, [PrinterEventType.HeartbeatMissing], threshold: null, window: null, autoResolve: true);
        var alertId = await SeedOpenAlertAsync(TenantA, ruleId, windowsClientId: _clientId);

        using (var context = CreateContext())
        {
            var client = await context.Set<WindowsClient>().IgnoreQueryFilters().SingleAsync(c => c.Id == _clientId);
            client.State = WindowsClientState.Active;
            await context.SaveChangesAsync();
        }

        var engine = CreateEngine(DateTimeOffset.UtcNow);
        var resolvedCount = await engine.AutoResolveOnceAsync(CancellationToken.None);

        Assert.Equal(1, resolvedCount);
        using var verifyContext = CreateContext();
        var alert = await verifyContext.Set<Alert>().IgnoreQueryFilters().SingleAsync(a => a.Id == alertId);
        Assert.Equal(AlertState.Resolved, alert.State);
    }

    private AlertEngine CreateEngine(DateTimeOffset now)
    {
        var options = Options.Create(new AlertingOptions { EventBatchSize = 500 });
        var factory = new TestSystemDbContextFactory(_connection);
        return new AlertEngine(factory, options, NullLogger<AlertEngine>.Instance, new FixedClock(now));
    }

    private async Task<Guid> SeedRuleAsync(
        Guid tenantId,
        IReadOnlyList<PrinterEventType> eventTypes,
        int? threshold,
        int? window,
        bool isActive = true,
        bool autoResolve = true)
    {
        using var context = CreateContext();
        var id = Guid.NewGuid();
        context.Set<AlertRule>().Add(new AlertRule
        {
            Id = id,
            TenantId = tenantId,
            Name = $"Regra {id:N}",
            IsActive = isActive,
            EventTypesCsv = string.Join(',', eventTypes.Select(t => (int)t)),
            ScopeType = AlertRuleScopeType.Tenant,
            Severity = AlertSeverity.Critica,
            ThresholdCount = threshold,
            ThresholdWindowMinutes = window,
            AutoResolve = autoResolve,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task SeedEventAsync(Guid tenantId, PrinterEventType type, Guid? printerId = null, Guid? clientId = null)
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Set<PrinterEvent>().Add(new PrinterEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PrinterId = printerId,
            WindowsClientId = clientId,
            Type = type,
            OccurredAt = now,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
        });
        await context.SaveChangesAsync();

        // Garante ticks distintos entre eventos seedados na mesma virada de relógio.
        await Task.Delay(2);
    }

    private async Task<Guid> SeedOpenAlertAsync(Guid tenantId, Guid ruleId, Guid? printerId = null, Guid? windowsClientId = null)
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        context.Set<Alert>().Add(new Alert
        {
            Id = id,
            TenantId = tenantId,
            AlertRuleId = ruleId,
            Severity = AlertSeverity.Critica,
            State = AlertState.Open,
            PrinterId = printerId,
            WindowsClientId = windowsClientId,
            FirstOccurrenceAt = now,
            FirstOccurrenceAtTicks = now.UtcTicks,
            LastOccurrenceAt = now,
            LastOccurrenceAtTicks = now.UtcTicks,
            OccurrenceCount = 1,
            CreatedAt = now,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private void SeedTenantA()
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

        _printerId = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = _printerId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.NoCommunication,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _clientId = Guid.NewGuid();
        context.Set<WindowsClient>().Add(new WindowsClient
        {
            Id = _clientId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            UniqueId = $"agent-{_clientId:N}",
            Hostname = "host-a",
            AgentVersion = "1.0.0",
            State = WindowsClientState.HeartbeatMissing,
            SecretHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class TestSystemDbContextFactory(SqliteConnection connection) : ISystemDbContextFactory
    {
        public AppDbContext Create()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            return new AppDbContext(options, SystemTenantContext.Instance);
        }
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
