using EasyPanel.Modules.Inventory;

namespace EasyPanel.Api.Controllers.Inventory;

/// <summary>Projeção de saída de um <see cref="InventoryItem"/> (Fase 5 — R1, R12.1).</summary>
public sealed record InventoryItemResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Sku,
    string Unit,
    string? SupplyLabel,
    bool IsActive,
    string? Observations,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Corpo da requisição de criação de um <see cref="InventoryItem"/> (R1.2, R12.1).</summary>
public sealed record CreateInventoryItemApiRequest(
    string Name,
    string? Sku,
    string? Unit,
    string? SupplyLabel,
    string? Observations);

/// <summary>Corpo da requisição de atualização de um <see cref="InventoryItem"/> (R1, R12.1).</summary>
public sealed record UpdateInventoryItemApiRequest(
    string Name,
    string? Sku,
    string? Unit,
    string? SupplyLabel,
    bool IsActive,
    string? Observations);

/// <summary>Página de resultados da listagem de itens (R1.4, R12.4).</summary>
public sealed record InventoryItemPageResponse(
    IReadOnlyList<InventoryItemResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
