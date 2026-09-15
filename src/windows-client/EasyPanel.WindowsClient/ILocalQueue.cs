namespace EasyPanel.WindowsClient;

/// <summary>Item de coleta enfileirado localmente para envio ao backend (R5.4).</summary>
/// <param name="IdempotencyKey">Chave de idempotência da submissão (dedup no backend — R13).</param>
/// <param name="Payload">Corpo JSON da submissão de coleta.</param>
/// <param name="EnqueuedAt">Momento em que foi enfileirado (UTC).</param>
public sealed record QueuedCollection(string IdempotencyKey, string Payload, DateTimeOffset EnqueuedAt);

/// <summary>
/// Fila local persistente do agente (R5.4): guarda coletas quando o backend está
/// inacessível e as reenvia ao restabelecer a conexão, com backoff. É durável
/// entre reinícios (persistida em SQLite local) e nunca armazena
/// credenciais/tokens/segredos SNMP em texto claro (R5.6).
/// </summary>
public interface ILocalQueue
{
    /// <summary>Enfileira uma coleta para envio posterior. Idempotente por chave.</summary>
    Task EnqueueAsync(QueuedCollection item, CancellationToken ct);

    /// <summary>Retorna até <paramref name="max"/> itens pendentes, dos mais antigos.</summary>
    Task<IReadOnlyList<QueuedCollection>> PeekAsync(int max, CancellationToken ct);

    /// <summary>Remove um item já confirmado pelo backend, pela chave de idempotência.</summary>
    Task AcknowledgeAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>Quantidade de itens pendentes na fila.</summary>
    Task<int> CountAsync(CancellationToken ct);
}
