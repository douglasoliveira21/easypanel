using EasyPanel.Infrastructure.Alerting;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Alerting;

/// <summary>
/// Testes do <see cref="AlertService"/> (Task 4.1 — R3.1-R3.8, R6.1-R6.2): transições
/// de estado válidas/inválidas, histórico de transições, cursor pagination e
/// isolamento por tenant.
/// </summary>
public sealed class AlertServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("39393939-3939-3939-3939-393939393939");
    private static readonly Guid TenantB = Guid.Parse("40404040-4040-4040-4040-404040404040");
    private static readonly Guid RuleId = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));

    public AlertServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedRule();
    }

    private void SeedRule()
    {
        using var context = CreateContext();
        context.Set<AlertRule>().Add(new AlertRule
        {
            Id = RuleId,
            TenantId = TenantA,
            Name = "Regra de teste",
            EventTypesCsv = "2",
            ScopeType = AlertRuleScopeType.Tenant,
            Severity = AlertSeverity.Critica,
            AutoResolve = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Acknowledge_OpenAlert_Transitions_AndAudits()
    {
        var alertId = await SeedAlertAsync(TenantA, AlertState.Open);
        _tenantContext.SetTenant(TenantA);
        var service = CreateService();

        var result = await service.AcknowledgeAsync(alertId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AlertState.Acknowledged, result.Value.State);
        Assert.Contains(_audit.Entries, e => e.Action == "alert.acknowledge");
    }

    [Fact]
    public async Task Acknowledge_AlreadyAcknowledged_Fails()
    {
        var alertId = await SeedAlertAsync(TenantA, AlertState.Acknowledged);
        _tenantContext.SetTenant(TenantA);
        var service = CreateService();

        var result = await service.AcknowledgeAsync(alertId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.InvalidAlertTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task Resolve_OpenAlert_Transitions_WithNote()
    {
        var alertId = await SeedAlertAsync(TenantA, AlertState.Open);
        _tenantContext.SetTenant(TenantA);
        var service = CreateService();

        var result = await service.ResolveAsync(alertId, new ResolveAlertRequest("Substituído o toner"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AlertState.Resolved, result.Value.State);
        Assert.Equal("Substituído o toner", result.Value.ResolutionNote);
        Assert.False(result.Value.AutoResolved);
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_Fails()
    {
        var alertId = await SeedAlertAsync(TenantA, AlertState.Resolved);
        _tenantContext.SetTenant(TenantA);
        var service = CreateService();

        var result = await service.ResolveAsync(alertId, new ResolveAlertRequest(null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.InvalidAlertTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_IncludesTransitionHistory()
    {
        var alertId = await SeedAlertAsync(TenantA, AlertState.Open);
        _tenantContext.SetTenant(TenantA);
        var service = CreateService();
        await service.AcknowledgeAsync(alertId, CancellationToken.None);

        var result = await service.GetAsync(alertId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Transitions);
        Assert.Single(result.Value.Transitions!);
        Assert.Equal(AlertState.Acknowledged, result.Value.Transitions![0].ToState);
    }

    [Fact]
    public async Task Get_CrossTenant_ReturnsNotFound()
    {
        var alertId = await SeedAlertAsync(TenantA, AlertState.Open);
        _tenantContext.SetTenant(TenantB);
        var service = CreateService();

        var result = await service.GetAsync(alertId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task List_CursorPagination_ReturnsPagesWithoutOverlap()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedAlertAsync(TenantA, AlertState.Open, _clock.GetUtcNow().AddMinutes(i));
        }

        _tenantContext.SetTenant(TenantA);
        var service = CreateService();

        var firstPage = await service.ListAsync(new AlertQuery(PageSize: 2), CancellationToken.None);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await service.ListAsync(
            new AlertQuery(Cursor: firstPage.Value.NextCursor, PageSize: 2), CancellationToken.None);
        Assert.Equal(2, secondPage.Value.Items.Count);

        var firstIds = firstPage.Value.Items.Select(a => a.Id).ToHashSet();
        Assert.DoesNotContain(secondPage.Value.Items, a => firstIds.Contains(a.Id));
    }

    private AlertService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private async Task<Guid> SeedAlertAsync(Guid tenantId, AlertState state, DateTimeOffset? occurredAt = null)
    {
        using var context = CreateContext();
        var now = occurredAt ?? _clock.GetUtcNow();
        var id = Guid.NewGuid();
        context.Set<Alert>().Add(new Alert
        {
            Id = id,
            TenantId = tenantId,
            AlertRuleId = RuleId,
            Severity = AlertSeverity.Critica,
            State = state,
            FirstOccurrenceAt = now,
            FirstOccurrenceAtTicks = now.UtcTicks,
            LastOccurrenceAt = now,
            LastOccurrenceAtTicks = now.UtcTicks,
            OccurrenceCount = 1,
            CreatedAt = now,
        });
        await context.SaveChangesAsync();
        return id;
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
