using System.Text.Json;
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
/// Testes do <see cref="CollectionProcessor"/> e do <see cref="CollectionProcessingWorker"/>
/// (Task 4.2/4.3 — R12, R14.2–R14.4): sucesso persiste contadores e status online e
/// atualiza a última coleta do agente; reprocessamento é idempotente; falha registra
/// evento e marca sem-comunicação; o worker drena as coletas pendentes.
/// </summary>
public sealed class CollectionProcessorTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("78787878-7878-7878-7878-787878787878");
    private static readonly Guid CustomerA = Guid.Parse("89898989-8989-8989-8989-898989898989");
    private static readonly Guid LocationA = Guid.Parse("90909090-9090-9090-9090-909090909090");

    private readonly SqliteConnection _connection;
    private readonly SuperAdminContext _tenantContext = new();
    private Guid _clientId;
    private Guid _printerId;

    public CollectionProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        (_clientId, _printerId) = SeedClientAndPrinter();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Process_Success_PersistsCountersAndStatus()
    {
        var collectionId = await SeedCollectionAsync(
            "col-1", success: true, counters: [new SubmittedCounter("BlackAndWhite", null, 1500)]);

        var processor = CreateProcessor();
        await processor.ProcessAsync(collectionId, CancellationToken.None);

        using var context = CreateContext();
        Assert.True(await context.Set<PrinterCounter>().IgnoreQueryFilters()
            .AnyAsync(c => c.PrinterId == _printerId && c.Value == 1500 && c.Source == CounterSource.Automatic));

        var printer = await context.Set<Printer>().IgnoreQueryFilters().SingleAsync(p => p.Id == _printerId);
        Assert.Equal(PrinterStatus.Online, printer.Status);

        var client = await context.Set<WindowsClient>().IgnoreQueryFilters().SingleAsync(c => c.Id == _clientId);
        Assert.NotNull(client.LastCollectionAt);

        Assert.True(await context.Set<IngestionDedup>().IgnoreQueryFilters()
            .AnyAsync(d => d.IdempotencyKey == "col-1"));
    }

    [Fact]
    public async Task Process_Idempotent_DoesNotDuplicateCounters()
    {
        var collectionId = await SeedCollectionAsync(
            "col-idem", success: true, counters: [new SubmittedCounter("BlackAndWhite", null, 2000)]);

        var processor = CreateProcessor();
        await processor.ProcessAsync(collectionId, CancellationToken.None);
        await processor.ProcessAsync(collectionId, CancellationToken.None);

        using var context = CreateContext();
        var count = await context.Set<PrinterCounter>().IgnoreQueryFilters()
            .CountAsync(c => c.CollectionId == collectionId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Process_Failure_RecordsEventAndNoCommunication()
    {
        var collectionId = await SeedCollectionAsync(
            "col-fail", success: false, counters: [], errors: "timeout SNMP");

        var processor = CreateProcessor();
        await processor.ProcessAsync(collectionId, CancellationToken.None);

        using var context = CreateContext();
        var generatedEvent = await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .SingleAsync(e => e.PrinterId == _printerId && e.Type == PrinterEventType.CollectionFailure);

        // CreatedAtTicks (Fase 3 — base do cursor do Motor_de_Alertas) deve ser
        // consistente com CreatedAt, para toda gravação de PrinterEvent.
        Assert.Equal(generatedEvent.CreatedAt.UtcTicks, generatedEvent.CreatedAtTicks);

        var printer = await context.Set<Printer>().IgnoreQueryFilters().SingleAsync(p => p.Id == _printerId);
        Assert.Equal(PrinterStatus.NoCommunication, printer.Status);
    }

    [Fact]
    public async Task Process_NonDecrease_IgnoresLowerReading()
    {
        var high = await SeedCollectionAsync(
            "col-high", success: true, counters: [new SubmittedCounter("BlackAndWhite", null, 5000)]);
        var low = await SeedCollectionAsync(
            "col-low", success: true, counters: [new SubmittedCounter("BlackAndWhite", null, 100)]);

        var processor = CreateProcessor();
        await processor.ProcessAsync(high, CancellationToken.None);
        await processor.ProcessAsync(low, CancellationToken.None);

        using var context = CreateContext();
        // A leitura de 100 (inferior a 5000) não deve ser persistida.
        Assert.False(await context.Set<PrinterCounter>().IgnoreQueryFilters()
            .AnyAsync(c => c.PrinterId == _printerId && c.Value == 100));
    }

    [Fact]
    public async Task Worker_Drains_PendingCollections()
    {
        await SeedCollectionAsync("w-1", success: true, counters: [new SubmittedCounter("BlackAndWhite", null, 10)]);
        await SeedCollectionAsync("w-2", success: true, counters: [new SubmittedCounter("Color", null, 20)]);

        var worker = new CollectionProcessingWorker(
            new TestSystemDbContextFactory(_connection),
            CreateProcessor(),
            Options.Create(new MonitoringOptions()),
            NullLogger<CollectionProcessingWorker>.Instance);

        var attempted = await worker.DrainOnceAsync(CancellationToken.None);

        Assert.Equal(2, attempted);
        using var context = CreateContext();
        Assert.Equal(2, await context.Set<IngestionDedup>().IgnoreQueryFilters().CountAsync());
    }

    // ---- Suprimentos (Fase 4 — R2.4/R4.3/R4.4) -----------------------------

    [Fact]
    public async Task Process_SupplyAboveThreshold_DoesNotGenerateEvent()
    {
        await SeedThresholdAsync(_printerId, "toner-preto", 10);
        var collectionId = await SeedCollectionAsync(
            "sup-1", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 50)]);

        var processor = CreateProcessor();
        await processor.ProcessAsync(collectionId, CancellationToken.None);

        using var context = CreateContext();
        Assert.True(await context.Set<SupplyReading>().IgnoreQueryFilters()
            .AnyAsync(r => r.PrinterId == _printerId && r.Label == "toner-preto" && r.Percent == 50));
        Assert.False(await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .AnyAsync(e => e.PrinterId == _printerId && e.Type == PrinterEventType.SupplyLow));
    }

    [Fact]
    public async Task Process_SupplyCrossesThreshold_GeneratesEventOnce()
    {
        await SeedThresholdAsync(_printerId, "toner-preto", 10);

        var first = await SeedCollectionAsync(
            "sup-2a", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 15)]);
        await CreateProcessor().ProcessAsync(first, CancellationToken.None);

        var second = await SeedCollectionAsync(
            "sup-2b", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 5)]);
        await CreateProcessor().ProcessAsync(second, CancellationToken.None);

        using var context = CreateContext();
        var eventCount = await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .CountAsync(e => e.PrinterId == _printerId && e.Type == PrinterEventType.SupplyLow);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task Process_SupplyRepeatedlyBelowThreshold_DoesNotDuplicateEvent()
    {
        await SeedThresholdAsync(_printerId, "toner-preto", 10);

        var first = await SeedCollectionAsync(
            "sup-3a", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 5)]);
        await CreateProcessor().ProcessAsync(first, CancellationToken.None);

        var second = await SeedCollectionAsync(
            "sup-3b", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 3)]);
        await CreateProcessor().ProcessAsync(second, CancellationToken.None);

        using var context = CreateContext();
        var eventCount = await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .CountAsync(e => e.PrinterId == _printerId && e.Type == PrinterEventType.SupplyLow);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task Process_SupplyBackAboveThenCrossesAgain_GeneratesNewEvent()
    {
        await SeedThresholdAsync(_printerId, "toner-preto", 10);

        var c1 = await SeedCollectionAsync(
            "sup-4a", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 5)]);
        await CreateProcessor().ProcessAsync(c1, CancellationToken.None);

        var c2 = await SeedCollectionAsync(
            "sup-4b", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 80)]);
        await CreateProcessor().ProcessAsync(c2, CancellationToken.None);

        var c3 = await SeedCollectionAsync(
            "sup-4c", success: true, counters: [], supplies: [new SubmittedSupply("toner-preto", 2)]);
        await CreateProcessor().ProcessAsync(c3, CancellationToken.None);

        using var context = CreateContext();
        var eventCount = await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .CountAsync(e => e.PrinterId == _printerId && e.Type == PrinterEventType.SupplyLow);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public async Task Process_SupplyWithoutThresholdConfigured_UsesPlatformDefault()
    {
        // Sem SupplyThreshold seedado: usa MonitoringOptions.DefaultSupplyThresholdPercent (10).
        var collectionId = await SeedCollectionAsync(
            "sup-5", success: true, counters: [], supplies: [new SubmittedSupply("cilindro", 8)]);

        await CreateProcessor().ProcessAsync(collectionId, CancellationToken.None);

        using var context = CreateContext();
        Assert.True(await context.Set<PrinterEvent>().IgnoreQueryFilters()
            .AnyAsync(e => e.PrinterId == _printerId && e.Type == PrinterEventType.SupplyLow));
    }

    private async Task SeedThresholdAsync(Guid printerId, string label, int thresholdPercent)
    {
        using var context = CreateContext();
        context.Set<SupplyThreshold>().Add(new SupplyThreshold
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            PrinterId = printerId,
            Label = label,
            ThresholdPercent = thresholdPercent,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private CollectionProcessor CreateProcessor() =>
        new(
            new TestSystemDbContextFactory(_connection),
            NullLogger<CollectionProcessor>.Instance,
            Options.Create(new MonitoringOptions()));

    private async Task<Guid> SeedCollectionAsync(
        string key,
        bool success,
        IReadOnlyList<SubmittedCounter> counters,
        string? errors = null,
        IReadOnlyList<SubmittedSupply>? supplies = null)
    {
        using var context = CreateContext();
        var id = Guid.NewGuid();
        context.Set<Collection>().Add(new Collection
        {
            Id = id,
            TenantId = TenantA,
            IdempotencyKey = key,
            WindowsClientId = _clientId,
            PrinterId = _printerId,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            FinishedAt = DateTimeOffset.UtcNow,
            Result = success ? CollectionResult.Success : CollectionResult.Failure,
            Errors = errors,
            CollectedData = JsonSerializer.Serialize(new CollectionPayload(counters, supplies ?? [])),
            AttemptCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private (Guid ClientId, Guid PrinterId) SeedClientAndPrinter()
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

        var clientId = Guid.NewGuid();
        context.Set<WindowsClient>().Add(new WindowsClient
        {
            Id = clientId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            UniqueId = $"agent-{clientId:N}",
            Hostname = "host-a",
            AgentVersion = "1.0.0",
            State = WindowsClientState.Active,
            SecretHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var printerId = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = printerId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        return (clientId, printerId);
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

    private sealed class SuperAdminContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSuperAdmin => true;

        public bool HasTenant => false;
    }
}
