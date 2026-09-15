using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Reporting;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Reporting;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Reporting;

/// <summary>
/// Testes do <see cref="BillingReportService"/> (Task 2.3 — R3): sobreposição
/// parcial de período é incluída, filtro por status, total calculado
/// corretamente, isolamento cross-tenant.
/// </summary>
public sealed class BillingReportServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid ContractA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();

    public BillingReportServiceTests()
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

    private static DateTimeOffset D(int month, int day) => new(2026, month, day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_PartialOverlap_IsIncluded()
    {
        // Fatura de Janeiro; consulta cobre só a última semana de janeiro até fevereiro.
        AddInvoice(InvoiceStatus.Emitida, 100m, D(1, 1), D(1, 31));

        var service = CreateService();
        var result = await service.GetAsync(
            new BillingReportQuery(D(1, 25), D(2, 28)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalInvoiceCount);
        Assert.Equal(100m, result.Value.GrandTotal);
    }

    [Fact]
    public async Task GetAsync_NoOverlap_IsExcluded()
    {
        AddInvoice(InvoiceStatus.Emitida, 100m, D(1, 1), D(1, 31));

        var service = CreateService();
        var result = await service.GetAsync(
            new BillingReportQuery(D(3, 1), D(3, 31)), CancellationToken.None);

        Assert.Equal(0, result.Value.TotalInvoiceCount);
        Assert.Empty(result.Value.Rows);
    }

    [Fact]
    public async Task GetAsync_FiltersByStatus()
    {
        AddInvoice(InvoiceStatus.Emitida, 100m, D(1, 1), D(1, 31));
        AddInvoice(InvoiceStatus.Cancelada, 50m, D(1, 1), D(1, 31));

        var service = CreateService();
        var result = await service.GetAsync(
            new BillingReportQuery(D(1, 1), D(1, 31), ReportingInvoiceStatus.Emitida), CancellationToken.None);

        Assert.Equal(1, result.Value.TotalInvoiceCount);
        Assert.Equal(100m, result.Value.GrandTotal);
    }

    [Fact]
    public async Task GetAsync_GroupsByCustomer_AndSumsTotal()
    {
        AddInvoice(InvoiceStatus.Emitida, 100m, D(1, 1), D(1, 31));
        AddInvoice(InvoiceStatus.Rascunho, 40m, D(1, 5), D(1, 31));

        var service = CreateService();
        var result = await service.GetAsync(new BillingReportQuery(D(1, 1), D(1, 31)), CancellationToken.None);

        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(CustomerA, row.CustomerId);
        Assert.Equal(2, row.InvoiceCount);
        Assert.Equal(140m, row.TotalAmount);
        Assert.Equal(140m, result.Value.GrandTotal);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_DoesNotLeak()
    {
        AddInvoice(InvoiceStatus.Emitida, 100m, D(1, 1), D(1, 31));

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.GetAsync(new BillingReportQuery(D(1, 1), D(1, 31)), CancellationToken.None);

        Assert.Empty(result.Value.Rows);
    }

    private void AddInvoice(InvoiceStatus status, decimal totalAmount, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Set<Invoice>().Add(new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            ContractId = ContractA,
            CustomerId = CustomerA,
            PeriodStart = periodStart,
            PeriodStartTicks = periodStart.UtcTicks,
            PeriodEnd = periodEnd,
            PeriodEndTicks = periodEnd.UtcTicks,
            Status = status,
            TotalAmount = totalAmount,
            GeneratedAt = now,
            GeneratedAtTicks = now.UtcTicks,
            CreatedAt = now,
        });
        context.SaveChanges();
    }

    private BillingReportService CreateService() => new(CreateContext());

    private void SeedTenant()
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        context.Set<Customer>().Add(new Customer
        {
            Id = CustomerA,
            TenantId = TenantA,
            RazaoSocial = "Cliente A",
            Cnpj = "11222333000181",
            Status = CustomerStatus.Ativo,
            CreatedAt = now,
        });
        context.Set<Contract>().Add(new Contract
        {
            Id = ContractA,
            TenantId = TenantA,
            Number = "C-001",
            CustomerId = CustomerA,
            StartDate = now,
            StartDateTicks = now.UtcTicks,
            Status = ContractStatus.Ativo,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
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
}
