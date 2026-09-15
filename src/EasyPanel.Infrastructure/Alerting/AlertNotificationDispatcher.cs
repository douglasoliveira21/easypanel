using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Drena a fila de despacho de notificação (<see cref="AlertNotificationOutbox"/>),
/// verifica <see cref="AlertSilence"/> vigente no momento do envio (R7.3), despacha
/// pelo <see cref="IAlertEmailSender"/>/<see cref="IAlertWebhookSender"/>
/// correspondente e registra cada tentativa em
/// <see cref="AlertNotificationAttempt"/> (R6), com retry por backoff exponencial
/// até <see cref="AlertingOptions.MaxNotificationAttempts"/> (R4.4/R5.5).
///
/// <para>Opera em contexto de sistema (Super Admin), como <c>AlertEngine</c>: cada
/// item processado permanece dentro do próprio <c>TenantId</c>.</para>
/// </summary>
public sealed class AlertNotificationDispatcher : BackgroundService
{
    private readonly ISystemDbContextFactory _contextFactory;
    private readonly IAlertEmailSender _emailSender;
    private readonly IAlertWebhookSender _webhookSender;
    private readonly AlertingOptions _options;
    private readonly ILogger<AlertNotificationDispatcher> _logger;
    private readonly TimeProvider _clock;

    public AlertNotificationDispatcher(
        ISystemDbContextFactory contextFactory,
        IAlertEmailSender emailSender,
        IAlertWebhookSender webhookSender,
        IOptions<AlertingOptions> options,
        ILogger<AlertNotificationDispatcher> logger,
        TimeProvider clock)
    {
        _contextFactory = contextFactory;
        _emailSender = emailSender;
        _webhookSender = webhookSender;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.DispatchIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na drenagem da fila de notificação de alertas.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Processa os itens pendentes elegíveis da fila. Público para permitir testes
    /// determinísticos.
    /// </summary>
    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        await using var context = _contextFactory.Create();

        var now = _clock.GetUtcNow();

        // NextAttemptAt (DateTimeOffset) não é comparável em SQL pelo provider
        // SQLite dos testes; o volume de itens Pending é tipicamente pequeno
        // (fila de despacho, não histórico), então filtra-se em memória.
        var pending = (await context.Set<AlertNotificationOutbox>()
            .Where(o => o.Status == OutboxStatus.Pending)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .Where(o => o.NextAttemptAt <= now)
            .OrderBy(o => o.NextAttemptAt)
            .ToList();

        var processed = 0;
        foreach (var item in pending)
        {
            await ProcessItemAsync(context, item, now, ct).ConfigureAwait(false);
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    private async Task ProcessItemAsync(
        AppDbContext context,
        AlertNotificationOutbox item,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var alert = await context.Set<Alert>().FirstOrDefaultAsync(a => a.Id == item.AlertId, ct).ConfigureAwait(false);
        if (alert is null)
        {
            item.Status = OutboxStatus.FailedPermanently;
            return;
        }

        var rule = await context.Set<AlertRule>().FirstOrDefaultAsync(r => r.Id == alert.AlertRuleId, ct)
            .ConfigureAwait(false);
        if (rule is null)
        {
            item.Status = OutboxStatus.FailedPermanently;
            return;
        }

        if (await IsSilencedAsync(context, alert, now, ct).ConfigureAwait(false))
        {
            item.Status = OutboxStatus.Suppressed;
            RecordAttempt(context, item, NotificationOutcome.Suppressed, null, null, now);
            return;
        }

        var summary = $"{rule.Name} — severidade {alert.Severity}, ocorrências: {alert.OccurrenceCount}, " +
            $"última ocorrência: {alert.LastOccurrenceAt:o}.";

        NotificationSendResult result = item.Channel switch
        {
            NotificationChannel.Email => await _emailSender.SendAsync(
                new AlertEmailMessage(ParseCsv(rule.EmailRecipientsCsv), rule.Name, alert.Severity.ToString(), summary, alert.Id),
                ct)
                .ConfigureAwait(false),
            NotificationChannel.Webhook => await _webhookSender.SendAsync(
                new AlertWebhookMessage(
                    rule.WebhookUrl ?? string.Empty,
                    rule.WebhookSecret ?? string.Empty,
                    alert.Id,
                    alert.TenantId,
                    alert.AlertRuleId,
                    alert.Severity.ToString(),
                    alert.PrinterId,
                    alert.WindowsClientId,
                    alert.LastOccurrenceAt,
                    summary),
                ct)
                .ConfigureAwait(false),
            _ => NotificationSendResult.Fail("Canal de notificação desconhecido."),
        };

        RecordAttempt(
            context,
            item,
            result.Success ? NotificationOutcome.Success : NotificationOutcome.Failure,
            result.HttpStatusCode,
            result.ErrorSummary,
            now);

        if (result.Success)
        {
            item.Status = OutboxStatus.Sent;
            return;
        }

        item.AttemptCount++;
        if (item.AttemptCount >= _options.MaxNotificationAttempts)
        {
            item.Status = OutboxStatus.FailedPermanently;
        }
        else
        {
            var backoffMinutes = Math.Min(60, Math.Pow(2, item.AttemptCount));
            item.NextAttemptAt = now.AddMinutes(backoffMinutes);
        }
    }

    private static void RecordAttempt(
        AppDbContext context,
        AlertNotificationOutbox item,
        NotificationOutcome outcome,
        int? httpStatusCode,
        string? errorSummary,
        DateTimeOffset now)
    {
        context.Set<AlertNotificationAttempt>().Add(new AlertNotificationAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            AlertId = item.AlertId,
            Channel = item.Channel,
            Outcome = outcome,
            AttemptNumber = item.AttemptCount + 1,
            HttpStatusCode = httpStatusCode,
            ErrorSummary = errorSummary,
            AttemptedAt = now,
            AttemptedAtTicks = now.UtcTicks,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// Verifica se existe um <see cref="AlertSilence"/> vigente para o Alerta no
    /// momento do envio (R7.3): um silenciamento se aplica quando, para cada campo
    /// informado (AlertRuleId/PrinterId/WindowsClientId), o valor coincide com o do
    /// Alerta — campos não informados no silenciamento são coringa.
    /// </summary>
    private static async Task<bool> IsSilencedAsync(AppDbContext context, Alert alert, DateTimeOffset now, CancellationToken ct)
    {
        // Filtro/ordenação por DateTimeOffset não é traduzido pelo provider SQLite
        // dos testes; o volume de silenciamentos por tenant é pequeno.
        var silences = await context.Set<AlertSilence>()
            .Where(s => s.TenantId == alert.TenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return silences.Any(s =>
            s.StartsAt <= now
            && (s.EndedEarlyAt ?? s.EndsAt) > now
            && (s.AlertRuleId is null || s.AlertRuleId == alert.AlertRuleId)
            && (s.PrinterId is null || s.PrinterId == alert.PrinterId)
            && (s.WindowsClientId is null || s.WindowsClientId == alert.WindowsClientId));
    }

    private static IReadOnlyList<string> ParseCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
