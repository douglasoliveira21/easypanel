using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Worker que consome coletas registradas e ainda não processadas, delegando cada
/// uma ao <see cref="ICollectionProcessor"/> (R14.2). Aplica retry implícito por
/// ciclo: uma coleta que falha permanece sem registro em <see cref="IngestionDedup"/>
/// e é reprocessada na próxima varredura, com <c>AttemptCount</c> incrementado,
/// sem descarte prematuro (R14.3/R12.4).
///
/// <para>Este worker é a implementação de fila baseada em polling sobre o próprio
/// PostgreSQL, mantendo o sistema em um único deployable. O design admite trocar
/// esta implementação por Hangfire/Redis sem alterar o <see cref="ICollectionProcessor"/>.</para>
/// </summary>
public sealed class CollectionProcessingWorker : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 10;

    private readonly ISystemDbContextFactory _contextFactory;
    private readonly ICollectionProcessor _processor;
    private readonly MonitoringOptions _options;
    private readonly ILogger<CollectionProcessingWorker> _logger;

    public CollectionProcessingWorker(
        ISystemDbContextFactory contextFactory,
        ICollectionProcessor processor,
        IOptions<MonitoringOptions> options,
        ILogger<CollectionProcessingWorker> logger)
    {
        _contextFactory = contextFactory;
        _processor = processor;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.HeartbeatScanIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no ciclo de processamento de coletas.");
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
    /// Processa um lote de coletas pendentes (sem dedup e abaixo do teto de
    /// tentativas). Público para testes determinísticos. Retorna quantas coletas
    /// foram tentadas.
    /// </summary>
    public async Task<int> DrainOnceAsync(CancellationToken ct)
    {
        List<Guid> pending;

        await using (var context = _contextFactory.Create())
        {
            // Coletas sem registro de dedup e ainda elegíveis (abaixo do teto de
            // tentativas). O anti-join por IdempotencyKey seleciona as pendentes.
            var processedKeys = context.Set<IngestionDedup>().Select(d => d.IdempotencyKey);

            // Sem ORDER BY por DateTimeOffset (nem todo provider o traduz, ex.:
            // SQLite dos testes). A ordem de drenagem não é crítica: todas as
            // coletas elegíveis do lote são processadas.
            pending = await context.Set<Collection>()
                .Where(c => c.AttemptCount < MaxAttempts && !processedKeys.Contains(c.IdempotencyKey))
                .Take(BatchSize)
                .Select(c => c.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        var attempted = 0;
        foreach (var id in pending)
        {
            ct.ThrowIfCancellationRequested();
            attempted++;
            try
            {
                await _processor.ProcessAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Retry sem descarte: fica para o próximo ciclo (R14.3).
                _logger.LogWarning(ex, "Coleta {CollectionId} será reprocessada.", id);
            }
        }

        return attempted;
    }
}
