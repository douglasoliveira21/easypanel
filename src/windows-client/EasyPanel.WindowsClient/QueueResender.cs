using Microsoft.Extensions.Logging;

namespace EasyPanel.WindowsClient;

/// <summary>
/// Reenvia as coletas persistidas na <see cref="ILocalQueue"/> ao backend quando a
/// conexão é restabelecida (R5.4/R5.5). Processa em lotes: cada item confirmado
/// (2xx) é removido da fila; itens que falham permanecem para a próxima tentativa,
/// com backoff aplicado pelo laço do host. Sem descarte prematuro.
/// </summary>
public sealed class QueueResender
{
    private const int BatchSize = 50;

    private readonly ILocalQueue _queue;
    private readonly IBackendClient _backend;
    private readonly ILogger<QueueResender> _logger;

    public QueueResender(ILocalQueue queue, IBackendClient backend, ILogger<QueueResender> logger)
    {
        _queue = queue;
        _backend = backend;
        _logger = logger;
    }

    /// <summary>
    /// Tenta reenviar um lote de itens pendentes. Retorna quantos foram confirmados
    /// e removidos. Ao primeiro item que falha, interrompe o lote (provável perda de
    /// conexão), deixando o restante para o próximo ciclo com backoff.
    /// </summary>
    public async Task<int> ResendBatchAsync(CancellationToken ct)
    {
        var pending = await _queue.PeekAsync(BatchSize, ct).ConfigureAwait(false);
        var acknowledged = 0;

        foreach (var item in pending)
        {
            ct.ThrowIfCancellationRequested();

            var ok = await _backend
                .SubmitCollectionAsync(item.IdempotencyKey, item.Payload, ct)
                .ConfigureAwait(false);

            if (!ok)
            {
                _logger.LogDebug(
                    "Reenvio interrompido; {Acknowledged} confirmado(s), restante permanece na fila.",
                    acknowledged);
                break;
            }

            await _queue.AcknowledgeAsync(item.IdempotencyKey, ct).ConfigureAwait(false);
            acknowledged++;
        }

        return acknowledged;
    }
}
