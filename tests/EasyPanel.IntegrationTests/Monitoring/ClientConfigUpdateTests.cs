using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes dos serviços de configuração e atualização do agente (Task 7.1/7.2 —
/// R15.1/R15.2, R16.1; Fase 4 — R1.2): config retornada pela identidade do agente,
/// incluindo as impressoras monitoráveis do Local (`MonitoredPrinters`); metadados
/// de atualização quando publicados; 404 quando ausente; 401 sem agente autenticado.
/// </summary>
public sealed class ClientConfigUpdateTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("6a6a6a6a-6a6a-6a6a-6a6a-6a6a6a6a6a6a");
    private static readonly Guid CustomerA = Guid.Parse("7b7b7b7b-7b7b-7b7b-7b7b-7b7b7b7b7b7b");
    private static readonly Guid LocationA = Guid.Parse("8c8c8c8c-8c8c-8c8c-8c8c-8c8c8c8c8c8c");

    private readonly SqliteConnection _connection;
    private readonly SuperAdminContext _tenantContext = new();

    public ClientConfigUpdateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Config_Authenticated_ReturnsInterval_AndMonitoredPrinters()
    {
        var monitored = await SeedMonitoredPrinterAsync();
        var ignored = await SeedIgnoredPrinterAsync();

        var options = Options.Create(new MonitoringOptions { DefaultCollectionIntervalSeconds = 123 });
        var service = new ClientConfigService(Authenticated(LocationA), options, CreateContext());

        var result = await service.GetAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(123, result.Value.CollectionIntervalSeconds);
        Assert.Empty(result.Value.DiscoveryTargets);

        var printers = result.Value.MonitoredPrinters!;
        Assert.Contains(printers, p => p.PrinterId == monitored);
        Assert.DoesNotContain(printers, p => p.PrinterId == ignored);
    }

    [Fact]
    public async Task Config_Unauthenticated_Fails()
    {
        var service = new ClientConfigService(Anonymous(), Options.Create(new MonitoringOptions()), CreateContext());

        var result = await service.GetAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MonitoringErrors.Unauthorized.Code, result.Error.Code);
    }

    [Fact]
    public async Task Update_WhenPublished_ReturnsMetadata()
    {
        var options = Options.Create(new MonitoringOptions
        {
            Update = new ClientUpdateOptions
            {
                Version = "2.1.0",
                PackageUrl = "https://minio/agent/2.1.0.zip",
                Sha256 = "abc",
                Signature = "sig",
            },
        });
        var service = new ClientUpdateService(Authenticated(LocationA), options);

        var result = await service.GetLatestAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("2.1.0", result.Value.Version);
        Assert.Equal("abc", result.Value.Sha256);
    }

    [Fact]
    public async Task Update_WhenNoVersion_NotFound()
    {
        var service = new ClientUpdateService(Authenticated(LocationA), Options.Create(new MonitoringOptions()));

        var result = await service.GetLatestAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MonitoringErrors.NotFound.Code, result.Error.Code);
    }

    private async Task<Guid> SeedMonitoredPrinterAsync()
    {
        using var context = CreateContext();
        await EnsureTenantAsync(context);

        var id = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = id,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Ip = "10.0.0.5",
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedIgnoredPrinterAsync()
    {
        using var context = CreateContext();
        await EnsureTenantAsync(context);

        var id = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = id,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Ip = "10.0.0.6",
            Status = PrinterStatus.Disabled,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task EnsureTenantAsync(AppDbContext context)
    {
        if (await context.Set<Customer>().IgnoreQueryFilters().AnyAsync(c => c.Id == CustomerA))
        {
            return;
        }

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
        await context.SaveChangesAsync();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private static IClientContext Authenticated(Guid locationId) => new FakeClientContext(true, locationId);

    private static IClientContext Anonymous() => new FakeClientContext(false, null);

    private sealed class FakeClientContext(bool authenticated, Guid? locationId) : IClientContext
    {
        public Guid? ClientId => authenticated ? Guid.NewGuid() : null;

        public Guid? TenantId => authenticated ? TenantA : null;

        public Guid? LocationId => authenticated ? locationId : null;

        public bool IsAuthenticated => authenticated;
    }

    private sealed class SuperAdminContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSuperAdmin => true;

        public bool HasTenant => false;
    }
}
