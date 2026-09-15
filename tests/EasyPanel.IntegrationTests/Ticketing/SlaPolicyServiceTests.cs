using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Ticketing;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Ticketing;

/// <summary>
/// Testes do <see cref="SlaPolicyService"/> (Task 3.3 — R4.1/R4.2/R4.7): upsert
/// cria/atualiza, valores fora do permitido, isolamento cross-tenant.
/// </summary>
public sealed class SlaPolicyServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));

    public SlaPolicyServiceTests()
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
    public async Task SetAsync_Creates_ThenUpdates_SamePolicy()
    {
        var service = CreateService();

        var created = await service.SetAsync(new SetSlaPolicyRequest(TicketPriority.Alta, 60, 480), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Contains(_audit.Entries, e => e.Action == "slapolicy.create");

        var updated = await service.SetAsync(new SetSlaPolicyRequest(TicketPriority.Alta, 30, 240), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal(created.Value.Id, updated.Value.Id);
        Assert.Equal(30, updated.Value.FirstResponseMinutes);
        Assert.Contains(_audit.Entries, e => e.Action == "slapolicy.update");

        var list = await service.ListAsync(CancellationToken.None);
        Assert.Single(list.Value);
    }

    [Fact]
    public async Task SetAsync_NonPositiveMinutes_Fails()
    {
        var service = CreateService();

        var result = await service.SetAsync(new SetSlaPolicyRequest(TicketPriority.Baixa, 0, 100), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.InvalidSlaPolicy.Code, result.Error.Code);
    }

    [Fact]
    public async Task SetAsync_ResolutionBelowFirstResponse_Fails()
    {
        var service = CreateService();

        var result = await service.SetAsync(new SetSlaPolicyRequest(TicketPriority.Media, 120, 60), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.InvalidSlaPolicy.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListAsync_CrossTenant_DoesNotLeak()
    {
        var serviceA = CreateService();
        await serviceA.SetAsync(new SetSlaPolicyRequest(TicketPriority.Urgente, 15, 60), CancellationToken.None);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var listB = await serviceB.ListAsync(CancellationToken.None);

        Assert.Empty(listB.Value);
    }

    private SlaPolicyService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

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
