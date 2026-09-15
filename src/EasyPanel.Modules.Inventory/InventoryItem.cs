using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Inventory;

/// <summary>
/// Item de catálogo de estoque controlado pelo Tenant (Fase 5 — R1): um
/// consumível (toner, cilindro, peça) contra o qual movimentações são
/// registradas.
/// </summary>
public class InventoryItem : TenantEntity
{
    /// <summary>Nome do item.</summary>
    public required string Name { get; set; }

    /// <summary>Código/SKU, quando houver.</summary>
    public string? Sku { get; set; }

    /// <summary>Unidade de medida (ex.: "unidade").</summary>
    public string Unit { get; set; } = "unidade";

    /// <summary>
    /// Rótulo de suprimento vinculado (Fase 5 — R4.1), referencial por
    /// convenção de nome ao <c>Label</c> de <c>SupplyReading</c>/<c>SupplyThreshold</c>
    /// (Fase 4) — sem chave estrangeira entre os dois fluxos.
    /// </summary>
    public string? SupplyLabel { get; set; }

    /// <summary>Indica se o item está ativo (R1.5): inativo bloqueia novas movimentações.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Observações livres.</summary>
    public string? Observations { get; set; }
}
