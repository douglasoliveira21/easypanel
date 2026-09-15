using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="PrinterService"/> (Task 5.1/5.2 — R9/R10): CRUD, filtros/
/// paginação, isolamento multi-tenant (cross-tenant → 404) e movimentação com
/// histórico append-only.
/// </summary>
public sealed class PrinterServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");
    private static readonly Guid LocationA2 = Guid.Parse("e5e5e5e5-e5e5-e5e5-e5e5-e5e5e5e5e5e5");
    private static readonly Guid CustomerB = Guid.Parse("f6f6f6f6-f6f6-f6f6-f6f6-f6f6f6f6f6f6");
    private static readonly Guid LocationB = Guid.Parse("17171717-1717-1717-1717-171717171717");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();

    public PrinterServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedCustomersAndLocations();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Create_Then_Get_Succeeds()
    {
        var service = CreateService();

        var created = await service.CreateAsync(
            new CreatePrinterRequest(CustomerA, LocationA, "HP", "M404", NumeroSerie: "SN1"),
            CancellationToken.None);

        Assert.True(created.IsSuccess);

        var got = await service.GetAsync(created.Value.Id, CancellationToken.None);
        Assert.True(got.IsSuccess);
        Assert.Equal("HP", got.Value.Fabricante);
        Assert.Equal(TenantA, got.Value.TenantId);
    }

    [Fact]
    public async Task Create_InvalidLocation_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreatePrinterRequest(CustomerA, LocationB, "HP", "M404"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MonitoringErrors.InvalidLocation.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_CrossTenant_ReturnsNotFound()
    {
        // Cria uma impressora no Tenant B.
        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();
        var createdB = await serviceB.CreateAsync(
            new CreatePrinterRequest(CustomerB, LocationB, "Canon", "iR"),
            CancellationToken.None);
        Assert.True(createdB.IsSuccess);

        // Do Tenant A, a impressora do B é indistinguível de inexistente (404).
        _tenantContext.SetTenant(TenantA);
        var serviceA = CreateService();
        var got = await serviceA.GetAsync(createdB.Value.Id, CancellationToken.None);

        Assert.True(got.IsFailure);
        Assert.Equal(MonitoringErrors.NotFound.Code, got.Error.Code);
    }

    [Fact]
    public async Task List_Paginates_AndFiltersByTenant()
    {
        var service = CreateService();
        for (var i = 0; i < 3; i++)
        {
            await service.CreateAsync(
                new CreatePrinterRequest(CustomerA, LocationA, "HP", $"M{i}"),
                CancellationToken.None);
        }

        var page = await service.ListAsync(new PrinterQuery(new PageRequest(1, 2)), CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Equal(3, page.Value.TotalCount);
        Assert.Equal(2, page.Value.Items.Count);
    }

    [Fact]
    public async Task Move_Transfer_UpdatesLocation_AndRecordsHistory()
    {
        var service = CreateService();
        var created = await service.CreateAsync(
            new CreatePrinterRequest(CustomerA, LocationA, "HP", "M1"),
            CancellationToken.None);

        var moved = await service.MoveAsync(
            created.Value.Id, MovementOperation.Transfer, LocationA2, CancellationToken.None);

        Assert.True(moved.IsSuccess);
        Assert.Equal(LocationA2, moved.Value.LocationId);

        using var context = CreateContext();
        var movements = await context.Set<PrinterMovement>().IgnoreQueryFilters()
            .Where(m => m.PrinterId == created.Value.Id)
            .ToListAsync();

        // Instalação inicial + transferência.
        Assert.Contains(movements, m => m.Operation == MovementOperation.Install);
        Assert.Contains(movements, m => m.Operation == MovementOperation.Transfer && m.ToLocationId == LocationA2);
    }

    [Fact]
    public async Task Move_Disable_Then_Reactivate()
    {
        var service = CreateService();
        var created = await service.CreateAsync(
            new CreatePrinterRequest(CustomerA, LocationA, "HP", "M1"),
            CancellationToken.None);

        var disabled = await service.MoveAsync(
            created.Value.Id, MovementOperation.Disable, null, CancellationToken.None);
        Assert.Equal(PrinterStatus.Disabled, disabled.Value.Status);
        Assert.False(disabled.Value.MonitoringEnabled);

        var reactivated = await service.MoveAsync(
            created.Value.Id, MovementOperation.Reactivate, null, CancellationToken.None);
        Assert.True(reactivated.Value.MonitoringEnabled);
    }

    private PrinterService CreateService() =>
        new(CreateContext(), _tenantContext, new FakeCurrentUser(), new NoopAuditLogger());

    private void SeedCustomersAndLocations()
    {
        // Chamado no construtor com o contexto já em modo Super Admin.
        using var context = CreateContext();
        context.Set<Customer>().AddRange(
            new Customer
            {
                Id = CustomerA,
                TenantId = TenantA,
                RazaoSocial = "Cliente A",
                Cnpj = "11222333000181",
                Status = CustomerStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new Customer
            {
                Id = CustomerB,
                TenantId = TenantB,
                RazaoSocial = "Cliente B",
                Cnpj = "04252011000110",
                Status = CustomerStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        context.Set<Location>().AddRange(
            new Location
            {
                Id = LocationA,
                TenantId = TenantA,
                CustomerId = CustomerA,
                Nome = "Local A",
                Status = LocationStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new Location
            {
                Id = LocationA2,
                TenantId = TenantA,
                CustomerId = CustomerA,
                Nome = "Local A2",
                Status = LocationStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new Location
            {
                Id = LocationB,
                TenantId = TenantB,
                CustomerId = CustomerB,
                Nome = "Local B",
                Status = LocationStatus.Ativo,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId => Guid.Parse("99999999-9999-9999-9999-999999999999");
    }

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public Task LogAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
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
