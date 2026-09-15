using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Inventory;

/// <summary>
/// Lançamento somente-adição de entrada, saída ou ajuste de um
/// <see cref="InventoryItem"/> num Local, em uma quantidade e num instante
/// (Fase 5 — R2). Nunca é alterado ou removido após criado.
/// </summary>
public class InventoryMovement : TenantEntity
{
    /// <summary>Item movimentado.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Local onde a movimentação ocorreu.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Tipo da movimentação.</summary>
    public InventoryMovementType Type { get; set; }

    /// <summary>
    /// Direção do ajuste; obrigatório quando <see cref="Type"/> é
    /// <see cref="InventoryMovementType.Ajuste"/>, nulo caso contrário.
    /// </summary>
    public AdjustmentDirection? AdjustmentDirection { get; set; }

    /// <summary>Quantidade movimentada, sempre um inteiro positivo (R2.2) — a direção vem de <see cref="Type"/>/<see cref="AdjustmentDirection"/>.</summary>
    public int Quantity { get; set; }

    /// <summary>Impressora relacionada, quando aplicável (R4.2) — geralmente uma Saída para troca de suprimento.</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>Motivo/justificativa; obrigatório quando <see cref="Type"/> é Ajuste (R2.5).</summary>
    public string? Reason { get; set; }

    /// <summary>Usuário que registrou a movimentação.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Momento da movimentação (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Ticks (UTC) de <see cref="OccurredAt"/>. Cursor portável (mesmo motivo de <c>PrinterCounter.TimestampTicks</c>).</summary>
    public long OccurredAtTicks { get; set; }
}
