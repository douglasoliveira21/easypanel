using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Limiar percentual configurável de suprimento (Fase 4), abaixo do qual um
/// <see cref="PrinterEvent"/> do tipo <see cref="PrinterEventType.SupplyLow"/> é
/// gravado. Resolvido em cascata pelo mais específico:
/// <c>(PrinterId, Label)</c> → <c>(null, Label)</c> (padrão do tenant para o
/// rótulo) → <c>(null, null)</c> (padrão geral do tenant) → padrão de plataforma
/// (quando nenhuma linha existe).
/// </summary>
public class SupplyThreshold : TenantEntity
{
    /// <summary>Impressora alvo; nulo = padrão do Tenant (todas as impressoras).</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>
    /// Rótulo do suprimento; nulo = aplica a qualquer rótulo não coberto por uma
    /// linha mais específica.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>Percentual limiar (0–100).</summary>
    public int ThresholdPercent { get; set; }
}
