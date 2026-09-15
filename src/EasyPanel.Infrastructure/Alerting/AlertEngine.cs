using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Motor de avaliação de alertas (R2): consome <see cref="PrinterEvent"/> de forma
/// incremental via um cursor único de plataforma (<see cref="AlertEngineCheckpoint"/>),
/// casa contra as <see cref="AlertRule"/> ativas do mesmo tenant do evento, aplica
/// limiar/janela quando configurado e cria/atualiza <see cref="Alert"/>. Também
/// resolve automaticamente Alertas cuja regra tem <see cref="AlertRule.AutoResolve"/>
/// verificando o estado corrente do alvo (ver "Resolução automática" no design —
/// desvio deliberado de depender de um evento específico de recuperação, que a
/// Fase 2 não emite para agentes).
///
/// <para>É o único componente que enxerga tanto <c>Modules.Monitoring</c> quanto
/// <c>Modules.Alerting</c>: os módulos de domínio não se referenciam entre si (R8.5).
/// Opera em contexto de sistema (Super Admin), como <c>HeartbeatMonitor</c>, mas
/// nunca combina dados de tenants distintos numa mesma avaliação — cada evento é
/// avaliado estritamente contra as regras do seu próprio <c>TenantId</c>.</para>
/// </summary>
public sealed class AlertEngine : BackgroundService
{
    private readonly ISystemDbContextFactory _contextFactory;
    private readonly AlertingOptions _options;
    private readonly ILogger<AlertEngine> _logger;
    private readonly TimeProvider _clock;

    public AlertEngine(
        ISystemDbContextFactory contextFactory,
        IOptions<AlertingOptions> options,
        ILogger<AlertEngine> logger,
        TimeProvider clock)
    {
        _contextFactory = contextFactory;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.EngineScanIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken).ConfigureAwait(false);
                await AutoResolveOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na avaliação do Motor_de_Alertas.");
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
    /// Processa até <see cref="AlertingOptions.EventBatchSize"/> <c>PrinterEvent</c>
    /// ainda não avaliados, avançando o checkpoint por evento processado (restart
    /// seguro: reprocessar após uma falha no meio do lote não duplica Alertas nem
    /// pula eventos — R2.1/R2.8). Público para permitir testes determinísticos.
    /// </summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct)
    {
        await using var context = _contextFactory.Create();

        var checkpoint = await GetOrCreateCheckpointAsync(context, ct).ConfigureAwait(false);

        // Comparação/ordenação por long (CreatedAtTicks) é portável; Id (Guid) é
        // usado apenas como desempate para eventos com o mesmo CreatedAtTicks
        // (ex.: dois PrinterEvent gravados na mesma SaveChanges).
        var candidates = await context.Set<PrinterEvent>()
            .Where(e => e.CreatedAtTicks >= checkpoint.LastProcessedEventTicks)
            .OrderBy(e => e.CreatedAtTicks)
            .ThenBy(e => e.Id)
            .Take(_options.EventBatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var pending = candidates
            .Where(e => e.CreatedAtTicks > checkpoint.LastProcessedEventTicks
                || (e.CreatedAtTicks == checkpoint.LastProcessedEventTicks && e.Id != checkpoint.LastProcessedEventId))
            .ToList();

        var processed = 0;
        foreach (var printerEvent in pending)
        {
            await ProcessEventAsync(context, printerEvent, ct).ConfigureAwait(false);

            checkpoint.LastProcessedEventTicks = printerEvent.CreatedAtTicks;
            checkpoint.LastProcessedEventId = printerEvent.Id;
            checkpoint.UpdatedAt = _clock.GetUtcNow();

            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    private async Task ProcessEventAsync(AppDbContext context, PrinterEvent printerEvent, CancellationToken ct)
    {
        var rules = await context.Set<AlertRule>()
            .Where(r => r.TenantId == printerEvent.TenantId && r.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rules.Count == 0)
        {
            return;
        }

        Guid? locationId = null;
        if (printerEvent.PrinterId is { } printerId)
        {
            locationId = await context.Set<Printer>()
                .Where(p => p.Id == printerId)
                .Select(p => (Guid?)p.LocationId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }
        else if (printerEvent.WindowsClientId is { } clientId)
        {
            locationId = await context.Set<WindowsClient>()
                .Where(c => c.Id == clientId)
                .Select(c => (Guid?)c.LocationId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
        }

        foreach (var rule in rules)
        {
            if (!MatchesEventType(rule, printerEvent.Type))
            {
                continue;
            }

            if (!MatchesScope(rule, printerEvent, locationId))
            {
                continue;
            }

            await ApplyRuleAsync(context, rule, printerEvent, ct).ConfigureAwait(false);
        }
    }

    private static bool MatchesEventType(AlertRule rule, PrinterEventType type)
    {
        var value = (int)type;
        foreach (var token in rule.EventTypesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var parsed) && parsed == value)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesScope(AlertRule rule, PrinterEvent printerEvent, Guid? locationId) => rule.ScopeType switch
    {
        AlertRuleScopeType.Tenant => true,
        AlertRuleScopeType.Location => locationId is not null && locationId == rule.ScopeLocationId,
        AlertRuleScopeType.Printer => printerEvent.PrinterId is not null && printerEvent.PrinterId == rule.ScopePrinterId,
        AlertRuleScopeType.WindowsClient =>
            printerEvent.WindowsClientId is not null && printerEvent.WindowsClientId == rule.ScopeWindowsClientId,
        _ => false,
    };

    private async Task ApplyRuleAsync(AppDbContext context, AlertRule rule, PrinterEvent printerEvent, CancellationToken ct)
    {
        var existing = await context.Set<Alert>()
            .Where(a => a.TenantId == printerEvent.TenantId
                && a.AlertRuleId == rule.Id
                && a.PrinterId == printerEvent.PrinterId
                && a.WindowsClientId == printerEvent.WindowsClientId
                && (a.State == AlertState.Open || a.State == AlertState.Acknowledged))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Ocorrência subsequente do mesmo Alerta: agrega, sem nova notificação
            // (R2.5 — evita spam de notificação a cada evento repetido).
            if (printerEvent.OccurredAt > existing.LastOccurrenceAt)
            {
                existing.LastOccurrenceAt = printerEvent.OccurredAt;
                existing.LastOccurrenceAtTicks = printerEvent.CreatedAtTicks;
            }

            existing.OccurrenceCount++;
            existing.UpdatedAt = _clock.GetUtcNow();
            return;
        }

        if (rule.ThresholdCount is { } thresholdCount)
        {
            // Janela calculada sobre CreatedAtTicks (portável): comparação sobre
            // DateTimeOffset não é traduzida pelo provider SQLite dos testes.
            var windowStartTicks = printerEvent.CreatedAtTicks
                - TimeSpan.FromMinutes(rule.ThresholdWindowMinutes ?? 0).Ticks;
            var eventTypeValues = rule.EventTypesCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToList();

            var countQuery = context.Set<PrinterEvent>()
                .Where(e => e.TenantId == printerEvent.TenantId
                    && eventTypeValues.Contains((int)e.Type)
                    && e.CreatedAtTicks >= windowStartTicks
                    && e.CreatedAtTicks <= printerEvent.CreatedAtTicks);

            countQuery = printerEvent.PrinterId is { } pid
                ? countQuery.Where(e => e.PrinterId == pid)
                : countQuery.Where(e => e.WindowsClientId == printerEvent.WindowsClientId);

            var occurrences = await countQuery.CountAsync(ct).ConfigureAwait(false);
            if (occurrences < thresholdCount)
            {
                return;
            }
        }

        var now = _clock.GetUtcNow();
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            TenantId = printerEvent.TenantId,
            AlertRuleId = rule.Id,
            Severity = rule.Severity,
            State = AlertState.Open,
            PrinterId = printerEvent.PrinterId,
            WindowsClientId = printerEvent.WindowsClientId,
            FirstOccurrenceAt = printerEvent.OccurredAt,
            FirstOccurrenceAtTicks = printerEvent.CreatedAtTicks,
            LastOccurrenceAt = printerEvent.OccurredAt,
            LastOccurrenceAtTicks = printerEvent.CreatedAtTicks,
            OccurrenceCount = 1,
            CreatedAt = now,
        };

        context.Set<Alert>().Add(alert);

        if (rule.EmailEnabled)
        {
            context.Set<AlertNotificationOutbox>().Add(new AlertNotificationOutbox
            {
                Id = Guid.NewGuid(),
                TenantId = printerEvent.TenantId,
                AlertId = alert.Id,
                Channel = NotificationChannel.Email,
                Status = OutboxStatus.Pending,
                NextAttemptAt = now,
                CreatedAt = now,
            });
        }

        if (rule.WebhookEnabled)
        {
            context.Set<AlertNotificationOutbox>().Add(new AlertNotificationOutbox
            {
                Id = Guid.NewGuid(),
                TenantId = printerEvent.TenantId,
                AlertId = alert.Id,
                Channel = NotificationChannel.Webhook,
                Status = OutboxStatus.Pending,
                NextAttemptAt = now,
                CreatedAt = now,
            });
        }
    }

    /// <summary>
    /// Resolve automaticamente Alertas <c>Open</c>/<c>Acknowledged</c> cuja regra
    /// tem <see cref="AlertRule.AutoResolve"/> verdadeiro, quando o estado corrente
    /// do alvo (Impressora/Agente) já está saudável (R2.6). Público para permitir
    /// testes determinísticos.
    /// </summary>
    public async Task<int> AutoResolveOnceAsync(CancellationToken ct)
    {
        await using var context = _contextFactory.Create();

        var candidates = await context.Set<Alert>()
            .Where(a => a.State == AlertState.Open || a.State == AlertState.Acknowledged)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var ruleIds = candidates.Select(a => a.AlertRuleId).Distinct().ToList();
        var autoResolveRuleIds = await context.Set<AlertRule>()
            .Where(r => ruleIds.Contains(r.Id) && r.AutoResolve)
            .Select(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var autoResolveSet = autoResolveRuleIds.ToHashSet();

        var eligible = candidates.Where(a => autoResolveSet.Contains(a.AlertRuleId)).ToList();
        if (eligible.Count == 0)
        {
            return 0;
        }

        var printerIds = eligible.Where(a => a.PrinterId is not null).Select(a => a.PrinterId!.Value).Distinct().ToList();
        var clientIds = eligible.Where(a => a.WindowsClientId is not null).Select(a => a.WindowsClientId!.Value).Distinct().ToList();

        var healthyPrinterIds = printerIds.Count == 0
            ? []
            : await context.Set<Printer>()
                .Where(p => printerIds.Contains(p.Id) && p.Status == PrinterStatus.Online)
                .Select(p => p.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var healthyPrinterSet = healthyPrinterIds.ToHashSet();

        var activeClientIds = clientIds.Count == 0
            ? []
            : await context.Set<WindowsClient>()
                .Where(c => clientIds.Contains(c.Id) && c.State == WindowsClientState.Active)
                .Select(c => c.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        var activeClientSet = activeClientIds.ToHashSet();

        var now = _clock.GetUtcNow();
        var resolvedCount = 0;

        foreach (var alert in eligible)
        {
            var isHealthy =
                (alert.PrinterId is { } pid && healthyPrinterSet.Contains(pid))
                || (alert.WindowsClientId is { } cid && activeClientSet.Contains(cid));

            if (!isHealthy)
            {
                continue;
            }

            var previousState = alert.State;
            alert.State = AlertState.Resolved;
            alert.ResolvedAt = now;
            alert.ResolvedByUserId = null;
            alert.AutoResolved = true;
            alert.UpdatedAt = now;

            context.Set<AlertTransition>().Add(new AlertTransition
            {
                Id = Guid.NewGuid(),
                TenantId = alert.TenantId,
                AlertId = alert.Id,
                FromState = previousState,
                ToState = AlertState.Resolved,
                ActorUserId = null,
                OccurredAt = now,
                Note = "Resolução automática: alvo voltou a um estado saudável.",
                CreatedAt = now,
            });

            resolvedCount++;
        }

        if (resolvedCount > 0)
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return resolvedCount;
    }

    private async Task<AlertEngineCheckpoint> GetOrCreateCheckpointAsync(AppDbContext context, CancellationToken ct)
    {
        var checkpoint = await context.Set<AlertEngineCheckpoint>().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (checkpoint is not null)
        {
            return checkpoint;
        }

        checkpoint = new AlertEngineCheckpoint
        {
            Id = Guid.NewGuid(),
            LastProcessedEventTicks = 0,
            LastProcessedEventId = Guid.Empty,
            CreatedAt = _clock.GetUtcNow(),
        };

        context.Set<AlertEngineCheckpoint>().Add(checkpoint);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return checkpoint;
    }
}
