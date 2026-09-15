using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Portal;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Portal;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Portal;

/// <summary>
/// Testes do <see cref="PortalInvoiceService"/> (Fase 10 — R5): listagem e
/// detalhe de Faturas restritos ao Cliente do <see cref="ICustomerContext"/>,
/// exclusão de Faturas em Rascunho, isolamento cruzado de Cliente e de tenant.
/// </summary>
public sealed class PortalInvoiceServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid CustomerA1 = Guid.Parse("c1c1c1c1-1111-1111-1111-c1c1c1c1c1c1");
    private static readonly Guid CustomerA2 = Guid.Parse("c2c2c2c2-2222-2222-2222-c2c2c2c2c2c2");
    private static readonly Guid PrinterA1 = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly MutableCustomerContext _customerContext = new();
    private Guid _emitidaInvoiceId;
    private Guid _rascunhoInvoiceId;
    private Guid _otherCustomerInvoiceId;

    public PortalInvoiceServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedInvoices();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task List_ReturnsOnlyEmittedInvoicesOfOwnCustomer()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.ListAsync(cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(_emitidaInvoiceId, item.Id);
        Assert.Equal(InvoiceStatus.Emitida, (InvoiceStatus)(int)item.Status);
    }

    [Fact]
    public async Task Get_ForDraftInvoiceOfOwnCustomer_ReturnsNotFound()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.GetAsync(_rascunhoInvoiceId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortalErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_ForInvoiceOfAnotherCustomer_ReturnsNotFound()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.GetAsync(_otherCustomerInvoiceId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortalErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_ForOwnEmittedInvoice_ReturnsLineItems()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.GetAsync(_emitidaInvoiceId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.LineItems);
        Assert.Equal(PrinterA1, item.PrinterId);
        Assert.Equal(200, item.ExcessQuantity);
    }

    private PortalInvoiceService CreateService() => new(CreateContext(), _customerContext);

    private void SeedInvoices()
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

        context.Set<Customer>().AddRange(
            new Customer { Id = CustomerA1, TenantId = TenantA, RazaoSocial = "Cliente A1", Cnpj = "11222333000181", CreatedAt = now },
            new Customer { Id = CustomerA2, TenantId = TenantA, RazaoSocial = "Cliente A2", Cnpj = "22333444000192", CreatedAt = now });

        var locationId = Guid.NewGuid();
        context.Set<Location>().Add(new Location
        {
            Id = locationId, TenantId = TenantA, CustomerId = CustomerA1, Nome = "Local A1", CreatedAt = now,
        });

        context.Set<Printer>().Add(new Printer
        {
            Id = PrinterA1, TenantId = TenantA, CustomerId = CustomerA1, LocationId = locationId,
            Status = PrinterStatus.Online, MonitoringEnabled = true, CreatedAt = now,
        });

        var contractA1 = Guid.NewGuid();
        var contractA2 = Guid.NewGuid();
        context.Set<Contract>().AddRange(
            new Contract
            {
                Id = contractA1, TenantId = TenantA, Number = "C-A1", CustomerId = CustomerA1,
                StartDate = periodStart, StartDateTicks = periodStart.UtcTicks,
                Status = ContractStatus.Ativo, CreatedAt = now, CreatedAtTicks = now.UtcTicks,
            },
            new Contract
            {
                Id = contractA2, TenantId = TenantA, Number = "C-A2", CustomerId = CustomerA2,
                StartDate = periodStart, StartDateTicks = periodStart.UtcTicks,
                Status = ContractStatus.Ativo, CreatedAt = now, CreatedAtTicks = now.UtcTicks,
            });

        _emitidaInvoiceId = Guid.NewGuid();
        _rascunhoInvoiceId = Guid.NewGuid();
        _otherCustomerInvoiceId = Guid.NewGuid();

        context.Set<Invoice>().AddRange(
            new Invoice
            {
                Id = _emitidaInvoiceId, TenantId = TenantA, ContractId = contractA1, CustomerId = CustomerA1,
                PeriodStart = periodStart, PeriodStartTicks = periodStart.UtcTicks,
                PeriodEnd = periodEnd, PeriodEndTicks = periodEnd.UtcTicks,
                Status = InvoiceStatus.Emitida, TotalAmount = 20.00m, GeneratedAt = now, CreatedAt = now,
            },
            new Invoice
            {
                Id = _rascunhoInvoiceId, TenantId = TenantA, ContractId = contractA1, CustomerId = CustomerA1,
                PeriodStart = periodStart, PeriodStartTicks = periodStart.UtcTicks,
                PeriodEnd = periodEnd, PeriodEndTicks = periodEnd.UtcTicks,
                Status = InvoiceStatus.Rascunho, TotalAmount = 5.00m, GeneratedAt = now, CreatedAt = now,
            },
            new Invoice
            {
                Id = _otherCustomerInvoiceId, TenantId = TenantA, ContractId = contractA2, CustomerId = CustomerA2,
                PeriodStart = periodStart, PeriodStartTicks = periodStart.UtcTicks,
                PeriodEnd = periodEnd, PeriodEndTicks = periodEnd.UtcTicks,
                Status = InvoiceStatus.Emitida, TotalAmount = 30.00m, GeneratedAt = now, CreatedAt = now,
            });

        context.Set<InvoiceLineItem>().Add(new InvoiceLineItem
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            InvoiceId = _emitidaInvoiceId,
            PrinterId = PrinterA1,
            CounterType = BillingCounterType.BlackAndWhite,
            ConsumedQuantity = 700,
            IncludedQuantity = 500,
            ExcessQuantity = 200,
            UnitPrice = 0.10m,
            LineAmount = 20.00m,
            CreatedAt = now,
        });

        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
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

    private sealed class MutableCustomerContext : ICustomerContext
    {
        public Guid? CustomerId { get; private set; }

        public bool HasCustomer => CustomerId.HasValue;

        public void SetCustomer(Guid customerId) => CustomerId = customerId;
    }
}
