using EasyPanel.Infrastructure.Inventory;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Inventory;

/// <summary>
/// Testes do <see cref="InventoryMovementService"/> (Task 3.2 — R2-R5): efeito no
/// saldo por tipo/direção, saída sem saldo suficiente, ajuste sem justificativa,
/// piso em zero no ajuste-decrease, item inativo/local/impressora inválidos,
/// cursor pagination, saldo por local, mínimo e itens abaixo do mínimo, isolamento
/// cross-tenant.
/// </summary>
public sealed class InventoryMovementServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid LocationA2 = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
    private Guid _itemId;
    private Guid _printerId;

    public InventoryMovementServiceTests()
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
    public async Task Register_Entrada_IncreasesBalance()
    {
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Entrada, null, 10, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var balance = await service.GetBalanceByLocationAsync(LocationA, CancellationToken.None);
        Assert.Equal(10, balance.Value.Single().Quantity);
        Assert.Contains(_audit.Entries, e => e.Action == "inventorymovement.register");
    }

    [Fact]
    public async Task Register_Saida_DecreasesBalance()
    {
        var service = CreateService();
        await Entrada(service, 10);

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Saida, null, 4, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var balance = await service.GetBalanceByLocationAsync(LocationA, CancellationToken.None);
        Assert.Equal(6, balance.Value.Single().Quantity);
    }

    [Fact]
    public async Task Register_Saida_InsufficientBalance_Fails()
    {
        var service = CreateService();
        await Entrada(service, 3);

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Saida, null, 5, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientBalance.Code, result.Error.Code);

        // Sem efeito parcial: saldo permanece o mesmo.
        var balance = await service.GetBalanceByLocationAsync(LocationA, CancellationToken.None);
        Assert.Equal(3, balance.Value.Single().Quantity);
    }

    [Fact]
    public async Task Register_AjusteWithoutReason_Fails()
    {
        var service = CreateService();
        await Entrada(service, 5);

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(
                _itemId, LocationA, InventoryMovementType.Ajuste, AdjustmentDirection.Decrease, 2, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.AdjustmentRequiresReason.Code, result.Error.Code);
    }

    [Fact]
    public async Task Register_AjusteDecrease_BeyondBalance_FloorsAtZero()
    {
        var service = CreateService();
        await Entrada(service, 3);

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(
                _itemId, LocationA, InventoryMovementType.Ajuste, AdjustmentDirection.Decrease, 10, null, "Contagem física"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var balance = await service.GetBalanceByLocationAsync(LocationA, CancellationToken.None);
        Assert.Equal(0, balance.Value.Single().Quantity);
    }

    [Fact]
    public async Task Register_AjusteIncrease_RaisesBalance_AndAudits()
    {
        var service = CreateService();
        await Entrada(service, 2);

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(
                _itemId, LocationA, InventoryMovementType.Ajuste, AdjustmentDirection.Increase, 5, null, "Contagem física encontrou mais itens"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var balance = await service.GetBalanceByLocationAsync(LocationA, CancellationToken.None);
        Assert.Equal(7, balance.Value.Single().Quantity);
    }

    [Fact]
    public async Task Register_InactiveItem_Fails()
    {
        var service = CreateService();
        var itemService = new InventoryItemService(CreateContext(), new FakeCurrentUser(), _audit);
        var item = await itemService.CreateAsync(
            new CreateInventoryItemRequest("Item inativo", null, null, null, null), CancellationToken.None);
        await itemService.UpdateAsync(
            item.Value.Id,
            new UpdateInventoryItemRequest(item.Value.Name, null, null, null, IsActive: false, null),
            CancellationToken.None);

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(item.Value.Id, LocationA, InventoryMovementType.Entrada, null, 1, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.ItemInactive.Code, result.Error.Code);
    }

    [Fact]
    public async Task Register_WithPrinter_LinksMovement_AndListsByPrinter()
    {
        var service = CreateService();

        var result = await service.RegisterAsync(
            new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Entrada, null, 5, null, null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);

        await service.RegisterAsync(
            new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Saida, null, 1, _printerId, "Troca de toner"),
            CancellationToken.None);

        var byPrinter = await service.ListByPrinterAsync(_printerId, null, 10, CancellationToken.None);
        Assert.True(byPrinter.IsSuccess);
        Assert.Single(byPrinter.Value.Items);
        Assert.Equal(_printerId, byPrinter.Value.Items[0].PrinterId);
    }

    [Fact]
    public async Task ListHistory_CursorPagination_ReturnsPagesWithoutOverlap()
    {
        var service = CreateService();
        for (var i = 0; i < 5; i++)
        {
            await service.RegisterAsync(
                new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Entrada, null, 1, null, null),
                CancellationToken.None);
        }

        var firstPage = await service.ListHistoryAsync(_itemId, LocationA, null, 2, CancellationToken.None);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await service.ListHistoryAsync(
            _itemId, LocationA, firstPage.Value.NextCursor, 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Value.Items.Count);

        var firstIds = firstPage.Value.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(secondPage.Value.Items, i => firstIds.Contains(i.Id));
    }

    [Fact]
    public async Task SetMinimum_ThenBelowMinimum_ListsCorrectly()
    {
        var service = CreateService();
        await Entrada(service, 2);

        var setResult = await service.SetMinimumAsync(
            new SetInventoryMinimumRequest(_itemId, LocationA, 5), CancellationToken.None);
        Assert.True(setResult.IsSuccess);
        Assert.Contains(_audit.Entries, e => e.Action == "inventoryminimum.create");

        var below = await service.ListBelowMinimumAsync(new PageRequest(1, 10), CancellationToken.None);
        Assert.True(below.IsSuccess);
        var entry = Assert.Single(below.Value.Items);
        Assert.Equal(2, entry.CurrentQuantity);
        Assert.Equal(5, entry.MinimumQuantity);

        // Após reabastecer acima do mínimo, deixa de aparecer.
        await Entrada(service, 10);
        var belowAfter = await service.ListBelowMinimumAsync(new PageRequest(1, 10), CancellationToken.None);
        Assert.Empty(belowAfter.Value.Items);
    }

    [Fact]
    public async Task GetBalanceByLocation_CrossTenant_NotFound()
    {
        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.GetBalanceByLocationAsync(LocationA, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryErrors.NotFound.Code, result.Error.Code);
    }

    private async Task Entrada(IInventoryMovementService service, int quantity) =>
        await service.RegisterAsync(
            new RegisterInventoryMovementRequest(_itemId, LocationA, InventoryMovementType.Entrada, null, quantity, null, null),
            CancellationToken.None);

    private InventoryMovementService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

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
        context.Set<Location>().Add(new Location
        {
            Id = LocationA2,
            TenantId = TenantA,
            CustomerId = CustomerA,
            Nome = "Local A2",
            Status = LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _printerId = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = _printerId,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _itemId = Guid.NewGuid();
        context.Set<InventoryItem>().Add(new InventoryItem
        {
            Id = _itemId,
            TenantId = TenantA,
            Name = "Toner de teste",
            Unit = "unidade",
            IsActive = true,
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

    /// <summary>
    /// Avança 1 tick a cada chamada — evita que movimentações registradas em
    /// sequência rápida no mesmo teste colidam em <c>OccurredAtTicks</c>, o que
    /// quebraria o filtro estrito "menor que o cursor" da paginação por cursor.
    /// </summary>
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
