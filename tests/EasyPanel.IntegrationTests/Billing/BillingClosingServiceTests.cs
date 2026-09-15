using EasyPanel.Infrastructure.Billing;
using EasyPanel.Infrastructure.Contracts;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Billing;

/// <summary>
/// Testes do <see cref="BillingClosingService"/> (Task 3.1 — R1/R2/R3/R4):
/// validação de período, refechamento bloqueado, cálculo de consumo (com e
/// sem leitura anterior ao período), piso em zero para consumo negativo,
/// resolução de contrato/franquia, geração de fatura consolidada,
/// isolamento cross-tenant.
/// </summary>
public sealed class BillingClosingServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid PrinterA = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");
    private static readonly Guid PrinterA2 = Guid.Parse("f6f6f6f6-6666-6666-6666-f6f6f6f6f6f6");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-02-05T00:00:00Z"));

    public BillingClosingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedTenant();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Execute_FuturePeriod_Fails()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 2), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingErrors.PeriodNotClosed.Code, result.Error.Code);
    }

    [Fact]
    public async Task Execute_InvalidMonth_Fails()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 13), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingErrors.InvalidMonth.Code, result.Error.Code);
    }

    [Fact]
    public async Task Execute_WithPriorReading_ComputesConsumption_AndGeneratesInvoice()
    {
        var contractId = await SetupActiveContractWithFranchiseAsync(
            BillingCounterType.BlackAndWhite, includedQuantity: 500, excessUnitPrice: 0.10m);

        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1000, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1700, Jan(20));

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.InvoiceCount);
        Assert.Contains(_audit.Entries, e => e.Action == "billingclosing.execute");

        var invoiceService = CreateInvoiceService();
        var list = await invoiceService.ListAsync(new InvoiceQuery(new PageRequest(1, 10)), CancellationToken.None);
        var invoice = Assert.Single(list.Value.Items);
        Assert.Equal(contractId, invoice.ContractId);

        var detail = await invoiceService.GetAsync(invoice.Id, CancellationToken.None);
        var item = Assert.Single(detail.Value.Items);
        // Consumo = 1700 - 1000 = 700; excedente = 700 - 500 = 200; valor = 200 * 0.10 = 20.00
        Assert.Equal(700, item.ConsumedQuantity);
        Assert.Equal(200, item.ExcessQuantity);
        Assert.Equal(20.00m, item.LineAmount);
        Assert.Equal(20.00m, invoice.TotalAmount);
    }

    [Fact]
    public async Task Execute_WithoutPriorReading_UsesFirstReadingWithinPeriod()
    {
        await SetupActiveContractWithFranchiseAsync(BillingCounterType.Color, includedQuantity: 0, excessUnitPrice: 1.00m);

        // Impressora nova: primeira leitura já dentro do período, sem leitura anterior.
        await AddReadingAsync(PrinterA, CounterType.Color, 100, Jan(5));
        await AddReadingAsync(PrinterA, CounterType.Color, 150, Jan(25));

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invoiceService = CreateInvoiceService();
        var list = await invoiceService.ListAsync(new InvoiceQuery(new PageRequest(1, 10)), CancellationToken.None);
        var detail = await invoiceService.GetAsync(list.Value.Items.Single().Id, CancellationToken.None);
        var item = Assert.Single(detail.Value.Items);

        // Consumo = 150 - 100 = 50 (não o valor acumulado total de 150).
        Assert.Equal(50, item.ConsumedQuantity);
    }

    [Fact]
    public async Task Execute_NegativeConsumption_TreatedAsZero_NoInvoiceGenerated()
    {
        await SetupActiveContractWithFranchiseAsync(BillingCounterType.BlackAndWhite, includedQuantity: 0, excessUnitPrice: 1.00m);

        // Impressora substituída: contador reinicia, sem ajuste administrativo.
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 5000, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 100, Jan(20));

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvoiceCount);
    }

    [Fact]
    public async Task Execute_NoApplicableContract_GeneratesNoInvoice()
    {
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 100, Jan(20));

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvoiceCount);
    }

    [Fact]
    public async Task Execute_NoFranchiseForCounterType_SkipsCounter()
    {
        // Contrato ativo, mas sem franquia configurada para nenhum tipo de contador.
        var contractService = CreateContractService();
        var contract = await contractService.CreateAsync(
            new CreateContractRequest("C-SemFranquia", CustomerA, Jan(1), null, null), CancellationToken.None);
        await contractService.ChangeStatusAsync(contract.Value.Id, new ChangeContractStatusRequest(ContractStatus.Ativo), CancellationToken.None);

        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1000, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1700, Jan(20));

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvoiceCount);
    }

    [Fact]
    public async Task Execute_ConsumptionBelowIncludedQuantity_GeneratesNoInvoice()
    {
        await SetupActiveContractWithFranchiseAsync(BillingCounterType.BlackAndWhite, includedQuantity: 1000, excessUnitPrice: 1.00m);

        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1000, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1100, Jan(20)); // consumo 100, abaixo da franquia de 1000

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.InvoiceCount);
    }

    [Fact]
    public async Task Execute_MultiplePrintersSameContract_ConsolidateIntoOneInvoice()
    {
        var contractId = await SetupActiveContractWithFranchiseAsync(
            BillingCounterType.BlackAndWhite, includedQuantity: 0, excessUnitPrice: 1.00m);

        // O helper já vincula PrinterA ao contrato; adiciona só a segunda impressora.
        var scope = CreateScopeService();
        await scope.AddPrinterAsync(contractId, PrinterA2, CancellationToken.None);

        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 0, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 100, Jan(20));
        await AddReadingAsync(PrinterA2, CounterType.BlackAndWhite, 0, Dec(20));
        await AddReadingAsync(PrinterA2, CounterType.BlackAndWhite, 50, Jan(20));

        var service = CreateService();
        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.InvoiceCount);

        var invoiceService = CreateInvoiceService();
        var list = await invoiceService.ListAsync(new InvoiceQuery(new PageRequest(1, 10)), CancellationToken.None);
        var invoice = Assert.Single(list.Value.Items);
        var detail = await invoiceService.GetAsync(invoice.Id, CancellationToken.None);
        Assert.Equal(2, detail.Value.Items.Count);
        Assert.Equal(150.00m, invoice.TotalAmount);
    }

    [Fact]
    public async Task Execute_AlreadyClosedPeriod_Fails()
    {
        await SetupActiveContractWithFranchiseAsync(BillingCounterType.BlackAndWhite, includedQuantity: 0, excessUnitPrice: 1.00m);
        var service = CreateService();
        await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        var result = await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingErrors.AlreadyClosed.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListAsync_CrossTenant_DoesNotLeak()
    {
        await SetupActiveContractWithFranchiseAsync(BillingCounterType.BlackAndWhite, includedQuantity: 0, excessUnitPrice: 1.00m);
        var service = CreateService();
        await service.ExecuteAsync(new ExecuteBillingClosingRequest(2026, 1), CancellationToken.None);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var list = await serviceB.ListAsync(new PageRequest(1, 10), CancellationToken.None);

        Assert.Empty(list.Value.Items);
    }

    private async Task<Guid> SetupActiveContractWithFranchiseAsync(
        BillingCounterType counterType, long includedQuantity, decimal excessUnitPrice)
    {
        var contractService = CreateContractService();
        var franchiseService = CreateFranchiseService();
        var scope = CreateScopeService();

        var contract = await contractService.CreateAsync(
            new CreateContractRequest("C-001", CustomerA, Jan(1), null, null), CancellationToken.None);
        await contractService.ChangeStatusAsync(contract.Value.Id, new ChangeContractStatusRequest(ContractStatus.Ativo), CancellationToken.None);
        await franchiseService.SetAsync(
            contract.Value.Id,
            new SetContractFranchiseRequest((ContractCounterType)(int)counterType, null, includedQuantity, excessUnitPrice),
            CancellationToken.None);
        await scope.AddPrinterAsync(contract.Value.Id, PrinterA, CancellationToken.None);

        return contract.Value.Id;
    }

    private async Task AddReadingAsync(Guid printerId, CounterType counterType, long value, DateTimeOffset timestamp)
    {
        using var context = CreateContext();
        context.Set<PrinterCounter>().Add(new PrinterCounter
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            PrinterId = printerId,
            Timestamp = timestamp,
            TimestampTicks = timestamp.UtcTicks,
            CounterType = counterType,
            Value = value,
            Source = CounterSource.Automatic,
            CreatedAt = timestamp,
        });
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private static DateTimeOffset Jan(int day) => new(2026, 1, day, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Dec(int day) => new(2025, 12, day, 12, 0, 0, TimeSpan.Zero);

    private BillingClosingService CreateService() =>
        new(CreateContext(), CreateContractService(), new FakeCurrentUser(), _audit, _clock);

    private InvoiceService CreateInvoiceService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private ContractService CreateContractService() =>
        new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private ContractScopeService CreateScopeService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private ContractFranchiseService CreateFranchiseService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private void SeedTenant()
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
        context.Set<Printer>().Add(new Printer
        {
            Id = PrinterA,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Printer>().Add(new Printer
        {
            Id = PrinterA2,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId => Guid.Parse("99999999-9999-9999-9999-999999999999");
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _current = start;

        public override DateTimeOffset GetUtcNow() => _current = _current.AddTicks(1);
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
