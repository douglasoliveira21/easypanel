using EasyPanel.Infrastructure.Alerting;
using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Alerting;

/// <summary>
/// Testes do <see cref="AlertNotificationDispatcher"/> (Task 6.4 — R4.3-R4.4,
/// R5.4-R5.5, R6.1, R7.3): sucesso marca <c>Sent</c>; falha reagenda com backoff;
/// esgotar tentativas marca <c>FailedPermanently</c>; alerta silenciado gera
/// <c>Suppressed</c> sem despachar.
/// </summary>
public sealed class AlertNotificationDispatcherTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("95959595-9595-9595-9595-959595959595");

    private readonly SqliteConnection _connection;
    private readonly SuperAdminContext _tenantContext = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));

    public AlertNotificationDispatcherTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task DispatchOnce_EmailSuccess_MarksSent_AndRecordsAttempt()
    {
        var (ruleId, alertId) = await SeedRuleAndAlertAsync(emailEnabled: true, webhookEnabled: false);
        await SeedOutboxAsync(alertId, NotificationChannel.Email);

        var dispatcher = CreateDispatcher(emailResult: NotificationSendResult.Ok());
        var processed = await dispatcher.DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        using var context = CreateContext();
        var outbox = await context.Set<AlertNotificationOutbox>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(OutboxStatus.Sent, outbox.Status);

        var attempt = await context.Set<AlertNotificationAttempt>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(NotificationOutcome.Success, attempt.Outcome);
        Assert.Equal(1, attempt.AttemptNumber);
    }

    [Fact]
    public async Task DispatchOnce_Failure_ReschedulesWithBackoff()
    {
        var (_, alertId) = await SeedRuleAndAlertAsync(emailEnabled: true, webhookEnabled: false);
        await SeedOutboxAsync(alertId, NotificationChannel.Email);

        var dispatcher = CreateDispatcher(emailResult: NotificationSendResult.Fail("erro simulado"));
        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        var outbox = await context.Set<AlertNotificationOutbox>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
        Assert.Equal(1, outbox.AttemptCount);
        Assert.True(outbox.NextAttemptAt > _clock.GetUtcNow());
    }

    [Fact]
    public async Task DispatchOnce_ExhaustsAttempts_MarksFailedPermanently()
    {
        var (_, alertId) = await SeedRuleAndAlertAsync(emailEnabled: true, webhookEnabled: false);
        var outboxId = await SeedOutboxAsync(alertId, NotificationChannel.Email, attemptCount: 4);

        var dispatcher = CreateDispatcher(emailResult: NotificationSendResult.Fail("erro simulado"), maxAttempts: 5);
        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        using var context = CreateContext();
        var outbox = await context.Set<AlertNotificationOutbox>().IgnoreQueryFilters().SingleAsync(o => o.Id == outboxId);
        Assert.Equal(OutboxStatus.FailedPermanently, outbox.Status);
    }

    [Fact]
    public async Task DispatchOnce_SilencedAlert_MarksSuppressed_WithoutCallingChannel()
    {
        var (ruleId, alertId) = await SeedRuleAndAlertAsync(emailEnabled: true, webhookEnabled: false);
        await SeedOutboxAsync(alertId, NotificationChannel.Email);
        await SeedSilenceAsync(ruleId);

        var callCount = 0;
        var dispatcher = CreateDispatcher(emailResult: NotificationSendResult.Ok(), onEmailCalled: () => callCount++);
        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        Assert.Equal(0, callCount);
        using var context = CreateContext();
        var outbox = await context.Set<AlertNotificationOutbox>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(OutboxStatus.Suppressed, outbox.Status);

        var attempt = await context.Set<AlertNotificationAttempt>().IgnoreQueryFilters().SingleAsync();
        Assert.Equal(NotificationOutcome.Suppressed, attempt.Outcome);
    }

    private AlertNotificationDispatcher CreateDispatcher(
        NotificationSendResult emailResult,
        int maxAttempts = 5,
        Action? onEmailCalled = null)
    {
        var options = Options.Create(new AlertingOptions { MaxNotificationAttempts = maxAttempts });
        var factory = new TestSystemDbContextFactory(_connection);
        return new AlertNotificationDispatcher(
            factory,
            new FakeEmailSender(emailResult, onEmailCalled),
            new FakeWebhookSender(NotificationSendResult.Ok()),
            options,
            NullLogger<AlertNotificationDispatcher>.Instance,
            _clock);
    }

    private async Task<(Guid RuleId, Guid AlertId)> SeedRuleAndAlertAsync(bool emailEnabled, bool webhookEnabled)
    {
        using var context = CreateContext();
        var ruleId = Guid.NewGuid();
        context.Set<AlertRule>().Add(new AlertRule
        {
            Id = ruleId,
            TenantId = TenantA,
            Name = "Regra de teste",
            EventTypesCsv = "2",
            ScopeType = AlertRuleScopeType.Tenant,
            Severity = AlertSeverity.Critica,
            AutoResolve = true,
            EmailEnabled = emailEnabled,
            EmailRecipientsCsv = emailEnabled ? "ops@example.com" : null,
            WebhookEnabled = webhookEnabled,
            WebhookUrl = webhookEnabled ? "https://example.com/hook" : null,
            WebhookSecret = webhookEnabled ? "segredo" : null,
            CreatedAt = _clock.GetUtcNow(),
        });

        var alertId = Guid.NewGuid();
        var now = _clock.GetUtcNow();
        context.Set<Alert>().Add(new Alert
        {
            Id = alertId,
            TenantId = TenantA,
            AlertRuleId = ruleId,
            Severity = AlertSeverity.Critica,
            State = AlertState.Open,
            FirstOccurrenceAt = now,
            FirstOccurrenceAtTicks = now.UtcTicks,
            LastOccurrenceAt = now,
            LastOccurrenceAtTicks = now.UtcTicks,
            OccurrenceCount = 1,
            CreatedAt = now,
        });

        await context.SaveChangesAsync();
        return (ruleId, alertId);
    }

    private async Task<Guid> SeedOutboxAsync(Guid alertId, NotificationChannel channel, int attemptCount = 0)
    {
        using var context = CreateContext();
        var id = Guid.NewGuid();
        context.Set<AlertNotificationOutbox>().Add(new AlertNotificationOutbox
        {
            Id = id,
            TenantId = TenantA,
            AlertId = alertId,
            Channel = channel,
            Status = OutboxStatus.Pending,
            AttemptCount = attemptCount,
            NextAttemptAt = _clock.GetUtcNow(),
            CreatedAt = _clock.GetUtcNow(),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task SeedSilenceAsync(Guid ruleId)
    {
        using var context = CreateContext();
        context.Set<AlertSilence>().Add(new AlertSilence
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            AlertRuleId = ruleId,
            StartsAt = _clock.GetUtcNow().AddHours(-1),
            EndsAt = _clock.GetUtcNow().AddHours(1),
            CreatedByUserId = Guid.NewGuid(),
            CreatedAt = _clock.GetUtcNow(),
        });
        await context.SaveChangesAsync();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FakeEmailSender(NotificationSendResult result, Action? onCalled) : IAlertEmailSender
    {
        public Task<NotificationSendResult> SendAsync(AlertEmailMessage message, CancellationToken ct)
        {
            onCalled?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeWebhookSender(NotificationSendResult result) : IAlertWebhookSender
    {
        public Task<NotificationSendResult> SendAsync(AlertWebhookMessage message, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class TestSystemDbContextFactory(SqliteConnection connection) : ISystemDbContextFactory
    {
        public AppDbContext Create()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            return new AppDbContext(options, SystemTenantContext.Instance);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SuperAdminContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSuperAdmin => true;

        public bool HasTenant => false;
    }
}
