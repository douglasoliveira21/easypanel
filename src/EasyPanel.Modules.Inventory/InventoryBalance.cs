using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Inventory;

/// <summary>
/// Saldo materializado corrente de um <see cref="InventoryItem"/> num Local
/// (Fase 5 — R3.1), mantido consistente com o histórico de
/// <see cref="InventoryMovement"/> a cada movimentação registrada, na mesma
/// operação atômica. Nunca é negativo, e não é exposto para escrita direta
/// pela API — só o <c>InventoryMovementService</c> o atualiza.
/// </summary>
public class InventoryBalance : TenantEntity
{
    /// <summary>Item ao qual este saldo se refere.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Local ao qual este saldo se refere.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Quantidade corrente.</summary>
    public int Quantity { get; set; }
}
