using EasyPanel.Shared.Kernel.Entities;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Registro de deduplicação de ingestão (R13): guarda as chaves de idempotência já
/// processadas por tenant, servindo de backstop além do índice único em
/// <see cref="Collection"/>.
/// </summary>
public class IngestionDedup : TenantEntity
{
    /// <summary>Chave de idempotência processada (R13.1).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Coleta resultante do processamento original (para ack equivalente — R13.4).</summary>
    public Guid CollectionId { get; set; }
}

/// <summary>Uma leitura de contador submetida pelo agente em uma coleta.</summary>
public sealed record SubmittedCounter(
    string CounterType,
    string? CounterTypeLabel,
    long Value);

/// <summary>Uma leitura de nível de suprimento submetida pelo agente em uma coleta (Fase 4).</summary>
public sealed record SubmittedSupply(string Label, int Percent);

/// <summary>
/// Submissão de coleta do agente (R12/R13): identifica a impressora, o resultado,
/// erros, os contadores lidos e os níveis de suprimento (Fase 4), carimbada com a
/// chave de idempotência.
/// </summary>
public sealed record ClientCollectionSubmission(
    string IdempotencyKey,
    Guid? PrinterId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Success,
    string? Errors,
    IReadOnlyList<SubmittedCounter> Counters,
    string? RawStatus,
    IReadOnlyList<SubmittedSupply>? Supplies = null);

/// <summary>Confirmação de ingestão retornada ao agente (R13.4/R14.1).</summary>
public sealed record IngestionAck(Guid CollectionId, bool Duplicate);

/// <summary>
/// Envelope serializado em <see cref="Collection.CollectedData"/> (Fase 4):
/// contadores e níveis de suprimento da submissão. Ausência de <c>Supplies</c> no
/// JSON (dado gravado antes da Fase 4) resolve para lista vazia na desserialização.
/// </summary>
public sealed record CollectionPayload(
    IReadOnlyList<SubmittedCounter> Counters,
    IReadOnlyList<SubmittedSupply> Supplies);

/// <summary>Serviço de ingestão idempotente e enfileiramento (R12/R13/R14).</summary>
public interface IIngestionService
{
    /// <summary>
    /// Recebe uma submissão de coleta, aplica idempotência e a enfileira para
    /// processamento assíncrono, retornando um ack sem aguardar a persistência
    /// final (R13/R14.1). Chave duplicada → ack equivalente sem recriar dados.
    /// </summary>
    Task<Result<IngestionAck>> SubmitAsync(ClientCollectionSubmission submission, CancellationToken ct);
}

/// <summary>
/// Processa uma coleta enfileirada, persistindo contadores/status/eventos de forma
/// atômica e idempotente (R14.2/R14.4). Consumido pelo worker.
/// </summary>
public interface ICollectionProcessor
{
    /// <summary>Processa a coleta identificada, de forma idempotente.</summary>
    Task ProcessAsync(Guid collectionId, CancellationToken ct);
}
