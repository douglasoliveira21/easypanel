using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Reporting;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Reporting;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Reporting;

/// <summary>
/// Testes do <see cref="PrintConsumptionReportService"/> (Task 2.2 — R2):
/// cálculo com/sem leitura anterior ao início do intervalo, filtro por
/// Cliente/Local, validação de datas, isolamento cross-tenant.
/// </summary>
public sealed class PrintConsumptionReportServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid PrinterA = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();

    public PrintConsumptionReportServiceTests()
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

    private static DateTimeOffset Jan(int day) => new(2026, 1, day, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Dec(int day) => new(2025, 12, day, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_WithPriorReading_ComputesConsumption()
    {
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1000, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 1700, Jan(20));

        var service = CreateService();
        var result = await service.GetAsync(
            new PrintConsumptionReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(700, row.Consumed);
        Assert.Equal(ReportingCounterType.BlackAndWhite, row.CounterType);
        Assert.Equal(CustomerA, row.CustomerId);
        Assert.Equal(LocationA, row.LocationId);
    }

    [Fact]
    public async Task GetAsync_WithoutPriorReading_UsesFirstReadingWithinRange()
    {
        await AddReadingAsync(PrinterA, CounterType.Color, 100, Jan(5));
        await AddReadingAsync(PrinterA, CounterType.Color, 150, Jan(25));

        var service = CreateService();
        var result = await service.GetAsync(
            new PrintConsumptionReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(50, row.Consumed);
    }

    [Fact]
    public async Task GetAsync_NoReadingsAtAll_ReturnsEmptyRows()
    {
        var service = CreateService();
        var result = await service.GetAsync(
            new PrintConsumptionReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Rows);
    }

    [Fact]
    public async Task GetAsync_FilterByCustomer_ExcludesOtherCustomers()
    {
        var otherCustomerId = Guid.NewGuid();
        var otherLocationId = Guid.NewGuid();
        var otherPrinterId = Guid.NewGuid();
        using (var context = CreateContext())
        {
            context.Set<Customer>().Add(new Customer
            {
                Id = otherCustomerId,
                TenantId = TenantA,
                RazaoSocial = "Outro Cliente",
                Cnpj = "11222333000260",
                Status = CustomerStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            context.Set<Location>().Add(new Location
            {
                Id = otherLocationId,
                TenantId = TenantA,
                CustomerId = otherCustomerId,
                Nome = "Outro Local",
                Status = LocationStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            context.Set<Printer>().Add(new Printer
            {
                Id = otherPrinterId,
                TenantId = TenantA,
                CustomerId = otherCustomerId,
                LocationId = otherLocationId,
                Status = PrinterStatus.Unknown,
                MonitoringEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            context.SaveChanges();
        }

        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 0, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 100, Jan(20));
        await AddReadingAsync(otherPrinterId, CounterType.BlackAndWhite, 0, Dec(20));
        await AddReadingAsync(otherPrinterId, CounterType.BlackAndWhite, 999, Jan(20));

        var service = CreateService();
        var result = await service.GetAsync(
            new PrintConsumptionReportQuery(Jan(1), Jan(31), CustomerId: CustomerA), CancellationToken.None);

        var row = Assert.Single(result.Value.Rows);
        Assert.Equal(PrinterA, row.PrinterId);
        Assert.Equal(100, row.Consumed);
    }

    [Fact]
    public async Task GetAsync_EndBeforeStart_Fails()
    {
        var service = CreateService();

        var result = await service.GetAsync(
            new PrintConsumptionReportQuery(Jan(31), Jan(1)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReportingErrors.InvalidDateRange.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetAsync_RangeTooLarge_Fails()
    {
        var service = CreateService();

        var result = await service.GetAsync(
            new PrintConsumptionReportQuery(Jan(1), Jan(1).AddDays(400)), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ReportingErrors.DateRangeTooLarge.Code, result.Error.Code);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_DoesNotLeak()
    {
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 0, Dec(20));
        await AddReadingAsync(PrinterA, CounterType.BlackAndWhite, 100, Jan(20));

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.GetAsync(
            new PrintConsumptionReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.Empty(result.Value.Rows);
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

    private PrintConsumptionReportService CreateService() => new(CreateContext());

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
