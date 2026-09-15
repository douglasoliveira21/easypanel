using EasyPanel.Infrastructure.Contracts;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Contracts;

/// <summary>
/// Testes do <see cref="ContractFranchiseService"/> (Task 3.3 — R3): upsert
/// cria/atualiza, valores negativos → 400, no máximo uma franquia por
/// (Contrato, CounterType), isolamento cross-tenant.
/// </summary>
public sealed class ContractFranchiseServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-15T00:00:00Z"));
    private Guid _contractId;

    public ContractFranchiseServiceTests()
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
    public async Task SetAsync_Creates_ThenUpdates_SameFranchise()
    {
        var service = CreateService();

        var created = await service.SetAsync(
            _contractId, new SetContractFranchiseRequest(ContractCounterType.BlackAndWhite, null, 1000, 0.05m), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Contains(_audit.Entries, e => e.Action == "contractfranchise.create");

        var updated = await service.SetAsync(
            _contractId, new SetContractFranchiseRequest(ContractCounterType.BlackAndWhite, null, 2000, 0.03m), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal(created.Value.Id, updated.Value.Id);
        Assert.Equal(2000, updated.Value.IncludedQuantity);
        Assert.Contains(_audit.Entries, e => e.Action == "contractfranchise.update");

        var list = await service.ListAsync(_contractId, CancellationToken.None);
        Assert.Single(list.Value);
    }

    [Fact]
    public async Task SetAsync_NegativeIncludedQuantity_Fails()
    {
        var service = CreateService();

        var result = await service.SetAsync(
            _contractId, new SetContractFranchiseRequest(ContractCounterType.Color, null, -1, 0.10m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.InvalidFranchise.Code, result.Error.Code);
    }

    [Fact]
    public async Task SetAsync_NegativeExcessPrice_Fails()
    {
        var service = CreateService();

        var result = await service.SetAsync(
            _contractId, new SetContractFranchiseRequest(ContractCounterType.Color, null, 100, -0.01m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.InvalidFranchise.Code, result.Error.Code);
    }

    [Fact]
    public async Task SetAsync_DifferentCounterTypes_CreateSeparateFranchises()
    {
        var service = CreateService();

        await service.SetAsync(_contractId, new SetContractFranchiseRequest(ContractCounterType.BlackAndWhite, null, 1000, 0.05m), CancellationToken.None);
        await service.SetAsync(_contractId, new SetContractFranchiseRequest(ContractCounterType.Color, null, 500, 0.20m), CancellationToken.None);

        var list = await service.ListAsync(_contractId, CancellationToken.None);
        Assert.Equal(2, list.Value.Count);
    }

    [Fact]
    public async Task ListAsync_CrossTenant_NotFound()
    {
        var service = CreateService();

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.ListAsync(_contractId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.NotFound.Code, result.Error.Code);
        _ = service;
    }

    private ContractFranchiseService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

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

        _contractId = Guid.NewGuid();
        context.Set<Contract>().Add(new Contract
        {
            Id = _contractId,
            TenantId = TenantA,
            Number = "C-001",
            CustomerId = CustomerA,
            StartDate = DateTimeOffset.UtcNow,
            StartDateTicks = DateTimeOffset.UtcNow.UtcTicks,
            Status = ContractStatus.Rascunho,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedAtTicks = DateTimeOffset.UtcNow.UtcTicks,
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
