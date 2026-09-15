using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Worker periódico que detecta agentes sem heartbeat dentro do limite
/// configurável e marca seu estado como <see cref="WindowsClientState.HeartbeatMissing"/>,
/// gerando um <see cref="PrinterEvent"/> de heartbeat ausente para consumo do
/// motor de alertas da Fase 3 (R6.4). Ao retornar (heartbeat recebido), o próprio
/// <see cref="HeartbeatService"/> restaura o estado para <c>Active</c> (R6.5).
///
/// <para>Varre todos os tenants usando um contexto de sistema (Super Admin),
/// pois é uma tarefa de plataforma sem contexto de requisição.</para>
/// </summary>
public sealed class HeartbeatMonitor : BackgroundService
{
    private readonly ISystemDbContextFactory _contextFactory;
    private readonly MonitoringOptions _options;
    private readonly ILogger<HeartbeatMonitor> _logger;
    private readonly TimeProvider _clock;

    public HeartbeatMonitor(
        ISystemDbContextFactory contextFactory,
        IOptions<MonitoringOptions> options,
        ILogger<HeartbeatMonitor> logger,
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
        var interval = TimeSpan.FromSeconds(_options.HeartbeatScanIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Uma varredura com falha não deve derrubar o worker: loga e tenta
                // novamente no próximo ciclo.
                _logger.LogError(ex, "Falha na varredura de heartbeat ausente.");
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
    /// Executa uma varredura: marca agentes atrasados como <c>HeartbeatMissing</c>
    /// e gera o evento correspondente. Idempotente por ciclo (só age em agentes
    /// que ainda não estão marcados). Público para permitir testes determinísticos.
    /// </summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var threshold = now.AddSeconds(-_options.HeartbeatMissingThresholdSeconds);

        await using var context = _contextFactory.Create();

        // Agentes ativos (ou recém-registrados) com heartbeat já registrado. A
        // comparação de <see cref="DateTimeOffset"/> é feita em memória: nem todo
        // provider a traduz para SQL (ex.: SQLite dos testes), e o volume por
        // varredura é pequeno. Nunca reavalia agentes desabilitados.
        var candidates = await context.Set<WindowsClient>()
            .Where(c =>
                (c.State == WindowsClientState.Active || c.State == WindowsClientState.Registered)
                && c.LastHeartbeatAt != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var stale = candidates
            .Where(c => c.LastHeartbeatAt < threshold)
            .ToList();

        foreach (var client in stale)
        {
            client.State = WindowsClientState.HeartbeatMissing;
            client.UpdatedAt = now;

            context.Set<PrinterEvent>().Add(new PrinterEvent
            {
                Id = Guid.NewGuid(),
                TenantId = client.TenantId,
                WindowsClientId = client.Id,
                Type = PrinterEventType.HeartbeatMissing,
                Detail = $"Agente {client.UniqueId} sem heartbeat desde {client.LastHeartbeatAt:o}.",
                OccurredAt = now,
                CreatedAt = now,
                CreatedAtTicks = now.UtcTicks,
            });
        }

        if (stale.Count > 0)
        {
            await context.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("{Count} agente(s) marcados como HeartbeatMissing.", stale.Count);
        }

        return stale.Count;
    }
}
