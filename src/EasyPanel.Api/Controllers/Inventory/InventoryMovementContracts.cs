using EasyPanel.Modules.Inventory;

namespace EasyPanel.Api.Controllers.Inventory;

/// <summary>Página baseada em cursor para o histórico de movimentações (Fase 5 — R3.3).</summary>
public sealed record InventoryCursorPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de saída de uma <see cref="InventoryMovement"/> (R2, R12.1).</summary>
public sealed record InventoryMovementResponse(
    Guid Id,
    Guid ItemId,
    Guid LocationId,
    InventoryMovementType Type,
    AdjustmentDirection? AdjustmentDirection,
    int Quantity,
    Guid? PrinterId,
    string? Reason,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt);

/// <summary>Corpo da requisição de registro de uma movimentação (R2.1, R12.1).</summary>
public sealed record RegisterInventoryMovementApiRequest(
    Guid ItemId,
    Guid LocationId,
    InventoryMovementType Type,
    AdjustmentDirection? AdjustmentDirection,
    int Quantity,
    Guid? PrinterId,
    string? Reason);

/// <summary>Saldo corrente de um Item num Local (R3.1/R3.2).</summary>
public sealed record InventoryBalanceResponse(Guid ItemId, Guid LocationId, int Quantity);

/// <summary>Projeção de saída de um <see cref="InventoryMinimum"/> (R5).</summary>
public sealed record InventoryMinimumResponse(Guid Id, Guid ItemId, Guid LocationId, int MinimumQuantity);

/// <summary>Corpo da requisição de upsert de estoque mínimo (R5.1).</summary>
public sealed record SetInventoryMinimumApiRequest(Guid ItemId, Guid LocationId, int MinimumQuantity);

/// <summary>Item/Local abaixo do mínimo configurado, com saldo corrente (R5.2).</summary>
public sealed record BelowMinimumResponse(Guid ItemId, Guid LocationId, int CurrentQuantity, int MinimumQuantity);

/// <summary>Página de resultados da listagem de itens abaixo do mínimo (R5.2, R12.4).</summary>
public sealed record BelowMinimumPageResponse(
    IReadOnlyList<BelowMinimumResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
