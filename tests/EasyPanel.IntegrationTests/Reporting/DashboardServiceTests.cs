using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Reporting;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Reporting;

/// <summary>
/// Testes do <see cref="DashboardService"/> (Task 2.1 — R1): tenant vazio
/// retorna zeros em todas as chaves; contagens corretas com dados variados;
/// isolamento cross-tenant.
/// </summary>
public sealed class DashboardServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-05-01T00:00:00Z"));

    public DashboardServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetOverview_EmptyTenant_ReturnsZerosForAllKeys()
    {
        var service = CreateService(0);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var overview = result.Value;
        Assert.All(overview.PrintersByStatus.Values, v => Assert.Equal(0, v));
        Assert.All(overview.AlertsByState.Values, v => Assert.Equal(0, v));
        Assert.All(overview.TicketsByStatus.Values, v => Assert.Equal(0, v));
        Assert.All(overview.InvoicesByStatus.Values, v => Assert.Equal(0, v));
        Assert.Equal(0, overview.LowStockItemCount);
        Assert.Equal(0m, overview.InvoicesPendingTotalAmount);

        // Todas as chaves do enum aparecem, mesmo vazio.
        Assert.Equal(Enum.GetValues<PrinterStatus>().Length, overview.PrintersByStatus.Count);
        Assert.Equal(Enum.GetValues<AlertState>().Length, overview.AlertsByState.Count);
        Assert.Equal(Enum.GetValues<TicketStatus>().Length, overview.TicketsByStatus.Count);
        Assert.Equal(Enum.GetValues<InvoiceStatus>().Length, overview.InvoicesByStatus.Count);
    }

    [Fact]
    public async Task GetOverview_CountsPrintersByStatus()
    {
        SeedCustomer();
        AddPrinter(PrinterStatus.Online);
        AddPrinter(PrinterStatus.Online);
        AddPrinter(PrinterStatus.Offline);

        var service = CreateService(0);
        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(2, result.Value.PrintersByStatus[nameof(PrinterStatus.Online)]);
        Assert.Equal(1, result.Value.PrintersByStatus[nameof(PrinterStatus.Offline)]);
        Assert.Equal(0, result.Value.PrintersByStatus[nameof(PrinterStatus.Unknown)]);
    }

    [Fact]
    public async Task GetOverview_OpenAlertsBySeverity_ExcludesResolved()
    {
        AddAlert(AlertState.Open, AlertSeverity.Critica);
        AddAlert(AlertState.Acknowledged, AlertSeverity.Atencao);
        AddAlert(AlertState.Resolved, AlertSeverity.Critica);

        var service = CreateService(0);
        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(1, result.Value.OpenAlertsBySeverity[nameof(AlertSeverity.Critica)]);
        Assert.Equal(1, result.Value.OpenAlertsBySeverity[nameof(AlertSeverity.Atencao)]);
        Assert.Equal(2, result.Value.AlertsByState[nameof(AlertState.Open)] + result.Value.AlertsByState[nameof(AlertState.Acknowledged)]);
        Assert.Equal(1, result.Value.AlertsByState[nameof(AlertState.Resolved)]);
    }

    [Fact]
    public async Task GetOverview_InvoicesPendingTotal_SumsRascunhoAndEmitida()
    {
        AddInvoice(InvoiceStatus.Rascunho, 100m);
        AddInvoice(InvoiceStatus.Emitida, 50m);
        AddInvoice(InvoiceStatus.Cancelada, 999m);

        var service = CreateService(0);
        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(150m, result.Value.InvoicesPendingTotalAmount);
        Assert.Equal(1, result.Value.InvoicesByStatus[nameof(InvoiceStatus.Rascunho)]);
        Assert.Equal(1, result.Value.InvoicesByStatus[nameof(InvoiceStatus.Emitida)]);
        Assert.Equal(1, result.Value.InvoicesByStatus[nameof(InvoiceStatus.Cancelada)]);
    }

    [Fact]
    public async Task GetOverview_LowStockItemCount_ComesFromInventoryService()
    {
        var service = CreateService(7);

        var result = await service.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(7, result.Value.LowStockItemCount);
    }

    [Fact]
    public async Task GetOverview_CrossTenant_DoesNotLeak()
    {
        AddPrinter(PrinterStatus.Online);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService(0);

        var result = await serviceB.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(0, result.Value.PrintersByStatus[nameof(PrinterStatus.Online)]);
    }

    private void SeedCustomer()
    {
        using var context = CreateContext();
        if (context.Set<EasyPanel.Modules.Customers.Customer>().Any(c => c.Id == CustomerA))
        {
            return;
        }

        context.Set<EasyPanel.Modules.Customers.Customer>().Add(new EasyPanel.Modules.Customers.Customer
        {
            Id = CustomerA,
            TenantId = TenantA,
            RazaoSocial = "Cliente A",
            Cnpj = "11222333000181",
            Status = EasyPanel.Modules.Customers.CustomerStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    private void AddPrinter(PrinterStatus status)
    {
        SeedCustomer();
        using var context = CreateContext();
        var locationId = Guid.NewGuid();
        context.Set<EasyPanel.Modules.Customers.Location>().Add(new EasyPanel.Modules.Customers.Location
        {
            Id = locationId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            Nome = "Local",
            Status = EasyPanel.Modules.Customers.LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Printer>().Add(new Printer
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = locationId,
            Status = status,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    private void AddAlert(AlertState state, AlertSeverity severity)
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var alertRuleId = Guid.NewGuid();
        context.Set<AlertRule>().Add(new AlertRule
        {
            Id = alertRuleId,
            TenantId = TenantA,
            Name = "Regra de teste",
            EventTypesCsv = "SupplyLow",
            ScopeType = AlertRuleScopeType.Tenant,
            Severity = severity,
            CreatedAt = now,
        });
        context.Set<Alert>().Add(new Alert
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            AlertRuleId = alertRuleId,
            Severity = severity,
            State = state,
            FirstOccurrenceAt = now,
            FirstOccurrenceAtTicks = now.UtcTicks,
            LastOccurrenceAt = now,
            LastOccurrenceAtTicks = now.UtcTicks,
            CreatedAt = now,
        });
        context.SaveChanges();
    }

    private void AddInvoice(InvoiceStatus status, decimal totalAmount)
    {
        SeedCustomer();
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;
        var contractId = Guid.NewGuid();
        context.Set<EasyPanel.Modules.Contracts.Contract>().Add(new EasyPanel.Modules.Contracts.Contract
        {
            Id = contractId,
            TenantId = TenantA,
            Number = $"C-{Guid.NewGuid():N}",
            CustomerId = CustomerA,
            StartDate = now,
            StartDateTicks = now.UtcTicks,
            Status = EasyPanel.Modules.Contracts.ContractStatus.Ativo,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
        });
        context.Set<Invoice>().Add(new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            ContractId = contractId,
            CustomerId = CustomerA,
            PeriodStart = now,
            PeriodStartTicks = now.UtcTicks,
            PeriodEnd = now,
            PeriodEndTicks = now.UtcTicks,
            Status = status,
            TotalAmount = totalAmount,
            GeneratedAt = now,
            GeneratedAtTicks = now.UtcTicks,
            CreatedAt = now,
        });
        context.SaveChanges();
    }

    private DashboardService CreateService(long lowStockCount) =>
        new(CreateContext(), new FakeInventoryMovementService(lowStockCount), _clock);

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => start;
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

    /// <summary>Fake que só implementa <see cref="ListBelowMinimumAsync"/> — o único método usado pelo painel.</summary>
    private sealed class FakeInventoryMovementService(long totalCount) : IInventoryMovementService
    {
        public Task<Result<InventoryMovementDto>> RegisterAsync(RegisterInventoryMovementRequest request, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListHistoryAsync(
            Guid itemId, Guid? locationId, string? cursor, int pageSize, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListByPrinterAsync(
            Guid printerId, string? cursor, int pageSize, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<Result<IReadOnlyList<InventoryBalanceDto>>> GetBalanceByLocationAsync(Guid locationId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<Result<InventoryMinimumDto>> SetMinimumAsync(SetInventoryMinimumRequest request, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<Result<PagedResult<BelowMinimumDto>>> ListBelowMinimumAsync(PageRequest page, CancellationToken ct) =>
            Task.FromResult(Result.Success(new PagedResult<BelowMinimumDto>([], page.Page, page.PageSize, totalCount)));
    }
}
