using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Inventory;

/// <summary>
/// Estoque mínimo configurável de um <see cref="InventoryItem"/> num Local
/// (Fase 5 — R5.1). Ausência de linha para um (Item, Local) equivale a nenhum
/// mínimo configurado (nunca considerado baixo).
/// </summary>
public class InventoryMinimum : TenantEntity
{
    /// <summary>Item ao qual este mínimo se refere.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Local ao qual este mínimo se refere.</summary>
    public Guid LocationId { get; set; }

    /// <summary>Quantidade mínima (≥ 0) abaixo da qual o (Item, Local) é considerado em nível baixo.</summary>
    public int MinimumQuantity { get; set; }
}
