using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Execução de coleta de dados por um <see cref="WindowsClient"/> sobre uma
/// impressora (R12). Carrega a <see cref="IdempotencyKey"/> que garante que o
/// reenvio da mesma coleta não duplique dados (R13).
/// </summary>
public class Collection : TenantEntity
{
    /// <summary>Chave de idempotência única por tenant da submissão (R13.1).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Agente que executou a coleta (R12.1).</summary>
    public Guid WindowsClientId { get; set; }

    /// <summary>Impressora coletada, quando aplicável (R12.1).</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>Horário de início da coleta (R12.1).</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Horário de fim da coleta (R12.1), quando concluída.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Resultado da coleta (R12.1).</summary>
    public CollectionResult Result { get; set; }

    /// <summary>Erros reportados na coleta (texto, sem segredos) (R12.1/R12.3).</summary>
    public string? Errors { get; set; }

    /// <summary>Payload dos dados coletados (JSON), quando aplicável (R12.1).</summary>
    public string? CollectedData { get; set; }

    /// <summary>Número de tentativas de processamento (R12.3).</summary>
    public int AttemptCount { get; set; }
}
