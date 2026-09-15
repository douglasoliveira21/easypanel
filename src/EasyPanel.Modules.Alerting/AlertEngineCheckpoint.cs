using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Cursor único de plataforma (cross-tenant) usado pelo Motor_de_Alertas para
/// varrer <c>PrinterEvent</c> de forma incremental, sem reavaliar eventos já
/// processados (R2.1/R2.8). Não é uma <see cref="TenantEntity"/> pelo mesmo motivo
/// de <c>AuditLog</c>: é um dado de plataforma sem tenant único associado.
/// Espera-se uma única linha nesta tabela.
/// </summary>
public class AlertEngineCheckpoint : BaseEntity
{
    /// <summary>Ticks (UTC) de <c>PrinterEvent.CreatedAtTicks</c> do último evento processado.</summary>
    public long LastProcessedEventTicks { get; set; }

    /// <summary>Identificador do último <c>PrinterEvent</c> processado (desempate de <see cref="LastProcessedEventTicks"/> iguais).</summary>
    public Guid LastProcessedEventId { get; set; }
}
