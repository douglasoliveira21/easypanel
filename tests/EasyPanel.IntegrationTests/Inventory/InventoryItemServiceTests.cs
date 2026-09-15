using EasyPanel.Infrastructure.Inventory;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Inventory;

/// <summary>
/// Testes do <see cref="InventoryItemService"/> (Task 3.1 — R1.1-R1.5): CRUD,
/// validação de nome obrigatório, desativação preserva histórico, isolamento
/// cross-tenant.
/// </summary>
public sealed class InventoryItemServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid TenantB = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();

    public InventoryItemServiceTests()
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
    public async Task Create_WithValidData_Succeeds_AndAudits()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateInventoryItemRequest("Toner HP CF410A Preto", "CF410A", "unidade", "toner-preto", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(TenantA, result.Value.TenantId);
        Assert.Contains(_audit.Entries, e => e.Action == "inventoryitem.create");
    }

    [Fact]
    public async Task Create_WithoutName_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateInventoryItemRequest("   ", null, null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.NameRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Update_Deactivate_PreservesItem_AndAudits()
    {
        var service = CreateService();
        var created = await service.CreateAsync(
            new CreateInventoryItemRequest("Cilindro Brother", null, null, null, null), CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.Value.Id,
            new UpdateInventoryItemRequest("Cilindro Brother", null, null, null, IsActive: false, null),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.False(updated.Value.IsActive);
        Assert.Contains(_audit.Entries, e => e.Action == "inventoryitem.update");

        var stillThere = await service.GetAsync(created.Value.Id, CancellationToken.None);
        Assert.True(stillThere.IsSuccess);
    }

    [Fact]
    public async Task List_SearchesByNameOrSku()
    {
        var service = CreateService();
        await service.CreateAsync(new CreateInventoryItemRequest("Toner HP CF410A", "CF410A", null, null, null), CancellationToken.None);
        await service.CreateAsync(new CreateInventoryItemRequest("Cilindro Brother DR630", "DR630", null, null, null), CancellationToken.None);

        var result = await service.ListAsync(
            new InventoryItemQuery(new PageRequest(1, 10), Search: "Toner"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Contains(result.Value.Items, i => i.Sku == "CF410A");
    }

    [Fact]
    public async Task Get_CrossTenant_ReturnsNotFound()
    {
        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();
        var created = await serviceB.CreateAsync(
            new CreateInventoryItemRequest("Item do tenant B", null, null, null, null), CancellationToken.None);

        _tenantContext.SetTenant(TenantA);
        var serviceA = CreateService();
        var result = await serviceA.GetAsync(created.Value.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.NotFound.Code, result.Error.Code);
    }

    private InventoryItemService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit);

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
