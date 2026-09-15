using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Inventory;

/// <summary>Página baseada em cursor para grandes volumes (R3.3), específica deste módulo
/// para não referenciar tipos de cursor de outros módulos.</summary>
public sealed record InventoryCursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de leitura de uma <see cref="InventoryMovement"/> (R2).</summary>
public sealed record InventoryMovementDto(
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

/// <summary>Dados de registro de uma movimentação (R2.1). Tenant/ator vêm do contexto.</summary>
public sealed record RegisterInventoryMovementRequest(
    Guid ItemId,
    Guid LocationId,
    InventoryMovementType Type,
    AdjustmentDirection? AdjustmentDirection,
    int Quantity,
    Guid? PrinterId,
    string? Reason);

/// <summary>Saldo corrente de um Item num Local (R3.1/R3.2).</summary>
public sealed record InventoryBalanceDto(Guid ItemId, Guid LocationId, int Quantity);

/// <summary>Projeção de leitura de um <see cref="InventoryMinimum"/> (R5).</summary>
public sealed record InventoryMinimumDto(Guid Id, Guid ItemId, Guid LocationId, int MinimumQuantity);

/// <summary>Dados de configuração (upsert) de estoque mínimo (R5.1).</summary>
public sealed record SetInventoryMinimumRequest(Guid ItemId, Guid LocationId, int MinimumQuantity);

/// <summary>Item, Local e saldo corrente de um (Item, Local) abaixo do mínimo configurado (R5.2).</summary>
public sealed record BelowMinimumDto(Guid ItemId, Guid LocationId, int CurrentQuantity, int MinimumQuantity);

/// <summary>Serviço de movimentação, saldo e estoque mínimo (R2/R3/R4/R5).</summary>
public interface IInventoryMovementService
{
    /// <summary>Registra uma movimentação (Entrada/Saída/Ajuste) e atualiza o saldo materializado na mesma operação (R2/R3.1).</summary>
    Task<Result<InventoryMovementDto>> RegisterAsync(RegisterInventoryMovementRequest request, CancellationToken ct);

    /// <summary>Histórico de movimentações de um Item, com filtro opcional por Local, por cursor pagination (R3.3).</summary>
    Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListHistoryAsync(
        Guid itemId,
        Guid? locationId,
        string? cursor,
        int pageSize,
        CancellationToken ct);

    /// <summary>Movimentações (tipicamente Saídas) que referenciaram uma Impressora, por cursor pagination (R4.3).</summary>
    Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListByPrinterAsync(
        Guid printerId,
        string? cursor,
        int pageSize,
        CancellationToken ct);

    /// <summary>Saldo corrente de cada Item já movimentado num Local (R3.2).</summary>
    Task<Result<IReadOnlyList<InventoryBalanceDto>>> GetBalanceByLocationAsync(Guid locationId, CancellationToken ct);

    /// <summary>Cria ou atualiza (upsert por Item+Local) um estoque mínimo, auditado (R5.1/R5.3).</summary>
    Task<Result<InventoryMinimumDto>> SetMinimumAsync(SetInventoryMinimumRequest request, CancellationToken ct);

    /// <summary>Lista os (Item, Local) do tenant cujo saldo está abaixo do mínimo configurado (R5.2).</summary>
    Task<Result<PagedResult<BelowMinimumDto>>> ListBelowMinimumAsync(PageRequest page, CancellationToken ct);
}
