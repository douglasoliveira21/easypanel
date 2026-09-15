using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Inventory;

/// <summary>Projeção de leitura de um <see cref="InventoryItem"/> (R1).</summary>
public sealed record InventoryItemDto(
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

/// <summary>Dados de criação de um <see cref="InventoryItem"/> (R1.2). Tenant vem do contexto.</summary>
public sealed record CreateInventoryItemRequest(
    string Name,
    string? Sku,
    string? Unit,
    string? SupplyLabel,
    string? Observations);

/// <summary>Dados de atualização de um <see cref="InventoryItem"/> (R1), incluindo ativação/desativação (R1.5).</summary>
public sealed record UpdateInventoryItemRequest(
    string Name,
    string? Sku,
    string? Unit,
    string? SupplyLabel,
    bool IsActive,
    string? Observations);

/// <summary>Filtro de listagem de <see cref="InventoryItem"/> (R1.4).</summary>
public sealed record InventoryItemQuery(PageRequest Page, string? Search = null, bool? IsActive = null);

/// <summary>Serviço de CRUD de <see cref="InventoryItem"/> (R1).</summary>
public interface IInventoryItemService
{
    /// <summary>Cria um item vinculado ao tenant do contexto (R1.2).</summary>
    Task<Result<InventoryItemDto>> CreateAsync(CreateInventoryItemRequest request, CancellationToken ct);

    /// <summary>Atualiza um item do próprio tenant (R1); cross-tenant → 404.</summary>
    Task<Result<InventoryItemDto>> UpdateAsync(Guid id, UpdateInventoryItemRequest request, CancellationToken ct);

    /// <summary>Consulta por id, restrito ao tenant (R1.4).</summary>
    Task<Result<InventoryItemDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lista itens com busca/paginação, restrito ao tenant (R1.4).</summary>
    Task<Result<PagedResult<InventoryItemDto>>> ListAsync(InventoryItemQuery query, CancellationToken ct);
}
