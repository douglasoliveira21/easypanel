using EasyPanel.Infrastructure.Billing;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Billing;

/// <summary>
/// Testes do <see cref="InvoiceService"/> (Task 3.2 — R5/R6): transições de
/// status válidas/inválidas, "Cancelada" é terminal, consulta com itens,
/// isolamento cross-tenant.
/// </summary>
public sealed class InvoiceServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid ContractA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid CustomerA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid PrinterA = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-02-05T00:00:00Z"));
    private Guid _invoiceId;

    public InvoiceServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedInvoice();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetAsync_ReturnsInvoiceWithItems()
    {
        var service = CreateService();

        var result = await service.GetAsync(_invoiceId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(InvoiceStatus.Rascunho, result.Value.Status);
    }

    [Fact]
    public async Task ChangeStatus_RascunhoToEmitida_Succeeds_AndSetsIssuedAt()
    {
        var service = CreateService();

        var result = await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Emitida), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Emitida, result.Value.Status);
        Assert.NotNull(result.Value.IssuedAt);
        Assert.Contains(_audit.Entries, e => e.Action == "invoice.status_change");
    }

    [Fact]
    public async Task ChangeStatus_RascunhoToCancelada_Succeeds_AndSetsCancelledAt()
    {
        var service = CreateService();

        var result = await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Cancelada), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Cancelada, result.Value.Status);
        Assert.NotNull(result.Value.CancelledAt);
    }

    [Fact]
    public async Task ChangeStatus_EmitidaToCancelada_Succeeds()
    {
        var service = CreateService();
        await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Emitida), CancellationToken.None);

        var result = await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Cancelada), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Cancelada, result.Value.Status);
    }

    [Fact]
    public async Task ChangeStatus_CanceladaIsTerminal()
    {
        var service = CreateService();
        await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Cancelada), CancellationToken.None);

        var result = await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Emitida), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingErrors.InvalidStatusTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task ChangeStatus_EmitidaToRascunho_Fails()
    {
        var service = CreateService();
        await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Emitida), CancellationToken.None);

        var result = await service.ChangeStatusAsync(_invoiceId, new ChangeInvoiceStatusRequest(InvoiceStatus.Rascunho), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingErrors.InvalidStatusTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_NotFound()
    {
        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.GetAsync(_invoiceId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BillingErrors.NotFound.Code, result.Error.Code);
    }

    private InvoiceService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private void SeedInvoice()
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
        var locationId = Guid.NewGuid();
        context.Set<Location>().Add(new Location
        {
            Id = locationId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            Nome = "Local A",
            Status = LocationStatus.Ativo,
            CreatedAt = now,
        });
        context.Set<Printer>().Add(new Printer
        {
            Id = PrinterA,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = locationId,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = now,
        });
        context.Set<Contract>().Add(new Contract
        {
            Id = ContractA,
            TenantId = TenantA,
            Number = "C-001",
            CustomerId = CustomerA,
            StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            StartDateTicks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks,
            Status = ContractStatus.Ativo,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
        });

        _invoiceId = Guid.NewGuid();
        context.Set<Invoice>().Add(new Invoice
        {
            Id = _invoiceId,
            TenantId = TenantA,
            ContractId = ContractA,
            CustomerId = CustomerA,
            PeriodStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero),
            Status = InvoiceStatus.Rascunho,
            TotalAmount = 20.00m,
            GeneratedAt = now,
            CreatedAt = now,
        });
        context.Set<InvoiceLineItem>().Add(new InvoiceLineItem
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            InvoiceId = _invoiceId,
            PrinterId = PrinterA,
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
