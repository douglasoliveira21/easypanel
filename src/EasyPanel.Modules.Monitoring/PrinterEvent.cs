using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Evento relevante do ciclo de vida ou operação de uma <see cref="Printer"/> ou
/// agente (R6.4/R12.3). Somente-adição. Serve de fonte para o motor de alertas da
/// Fase 3 (que não é construído aqui): heartbeat ausente e falha de coleta são
/// gravados como eventos para consumo posterior.
/// </summary>
public class PrinterEvent : TenantEntity
{
    /// <summary>Impressora relacionada, quando aplicável (nulo para eventos de agente).</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>Agente relacionado, quando aplicável (ex.: heartbeat ausente).</summary>
    public Guid? WindowsClientId { get; set; }

    /// <summary>Tipo do evento (R6.4/R12.3).</summary>
    public PrinterEventType Type { get; set; }

    /// <summary>Detalhe/descrição do evento (sem dados sensíveis).</summary>
    public string? Detail { get; set; }

    /// <summary>Momento de ocorrência do evento (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Ticks (UTC) de <see cref="BaseEntity.CreatedAt"/>. Coluna portável usada
    /// pelo Motor_de_Alertas da Fase 3 como cursor de leitura incremental — o
    /// provider SQLite dos testes não traduz comparação sobre
    /// <see cref="DateTimeOffset"/> (ver nota SQLite do projeto).
    /// </summary>
    public long CreatedAtTicks { get; set; }
}
