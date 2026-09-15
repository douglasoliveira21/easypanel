using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Portal;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Portal;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Portal;

/// <summary>
/// Testes do <see cref="PortalFleetService"/> (Fase 10 — R3): listagem do
/// parque e histórico de contadores restritos ao Cliente do
/// <see cref="ICustomerContext"/>, isolamento cruzado de Cliente e de tenant.
/// </summary>
public sealed class PortalFleetServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid CustomerA1 = Guid.Parse("c1c1c1c1-1111-1111-1111-c1c1c1c1c1c1");
    private static readonly Guid CustomerA2 = Guid.Parse("c2c2c2c2-2222-2222-2222-c2c2c2c2c2c2");
    private static readonly Guid PrinterA1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PrinterA2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly MutableCustomerContext _customerContext = new();

    public PortalFleetServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedFleet();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ListPrinters_ReturnsOnlyOwnCustomerPrinters()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.ListPrintersAsync(cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(PrinterA1, item.Id);
        Assert.Equal("Local A1", item.LocationName);
    }

    [Fact]
    public async Task GetCounterHistory_ForPrinterOfAnotherCustomer_ReturnsNotFound()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        // PrinterA2 pertence ao CustomerA2, não ao CustomerA1 do contexto.
        var result = await service.GetCounterHistoryAsync(PrinterA2, cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortalErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetCounterHistory_ForOwnPrinter_ReturnsReadings()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.GetCounterHistoryAsync(PrinterA1, cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(1000, result.Value.Items[0].Value);
    }

    [Fact]
    public async Task ListPrinters_WithoutCustomerContext_ReturnsEmpty()
    {
        var service = CreateService();

        var result = await service.ListPrintersAsync(cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    private PortalFleetService CreateService() => new(CreateContext(), _customerContext);

    private void SeedFleet()
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;

        context.Set<Customer>().AddRange(
            new Customer { Id = CustomerA1, TenantId = TenantA, RazaoSocial = "Cliente A1", Cnpj = "11222333000181", CreatedAt = now },
            new Customer { Id = CustomerA2, TenantId = TenantA, RazaoSocial = "Cliente A2", Cnpj = "22333444000192", CreatedAt = now });

        var locationA1 = Guid.NewGuid();
        var locationA2 = Guid.NewGuid();
        context.Set<Location>().AddRange(
            new Location { Id = locationA1, TenantId = TenantA, CustomerId = CustomerA1, Nome = "Local A1", CreatedAt = now },
            new Location { Id = locationA2, TenantId = TenantA, CustomerId = CustomerA2, Nome = "Local A2", CreatedAt = now });

        context.Set<Printer>().AddRange(
            new Printer
            {
                Id = PrinterA1, TenantId = TenantA, CustomerId = CustomerA1, LocationId = locationA1,
                Status = PrinterStatus.Online, MonitoringEnabled = true, CreatedAt = now,
            },
            new Printer
            {
                Id = PrinterA2, TenantId = TenantA, CustomerId = CustomerA2, LocationId = locationA2,
                Status = PrinterStatus.Offline, MonitoringEnabled = true, CreatedAt = now,
            });

        context.Set<PrinterCounter>().Add(new PrinterCounter
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            PrinterId = PrinterA1,
            Timestamp = now,
            TimestampTicks = now.UtcTicks,
            CounterType = CounterType.BlackAndWhite,
            Value = 1000,
            Source = CounterSource.Automatic,
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
