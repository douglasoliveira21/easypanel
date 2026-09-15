using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Inventory;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Inventory;

/// <summary>
/// Implementação de <see cref="IInventoryItemService"/> (Fase 5 — R1). Como
/// <see cref="InventoryItem"/> é uma <c>TenantEntity</c>, conta com o isolamento
/// automático do ORM (filtro global de consulta + interceptor de escrita), no
/// mesmo padrão de <c>LocationService</c>/<c>AlertRuleService</c>.
/// </summary>
public sealed class InventoryItemService : IInventoryItemService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;

    public InventoryItemService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    /// <inheritdoc />
    public async Task<Result<InventoryItemDto>> CreateAsync(CreateInventoryItemRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<InventoryItemDto>(InventoryErrors.NameRequired);
        }

        var now = DateTimeOffset.UtcNow;
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Sku = request.Sku,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "unidade" : request.Unit.Trim(),
            SupplyLabel = request.SupplyLabel,
            IsActive = true,
            Observations = request.Observations,
            CreatedAt = now,
        };

        _context.Set<InventoryItem>().Add(item);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = item.TenantId,
                Action = "inventoryitem.create",
                ResourceType = nameof(InventoryItem),
                ResourceId = item.Id.ToString(),
                NewValues = Summarize(item),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(item));
    }

    /// <inheritdoc />
    public async Task<Result<InventoryItemDto>> UpdateAsync(Guid id, UpdateInventoryItemRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<InventoryItemDto>(InventoryErrors.NameRequired);
        }

        var item = await _context.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == id, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<InventoryItemDto>(InventoryErrors.NotFound);
        }

        var before = Summarize(item);

        item.Name = request.Name.Trim();
        item.Sku = request.Sku;
        item.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "unidade" : request.Unit.Trim();
        item.SupplyLabel = request.SupplyLabel;
        item.IsActive = request.IsActive;
        item.Observations = request.Observations;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = item.TenantId,
                Action = "inventoryitem.update",
                ResourceType = nameof(InventoryItem),
                ResourceId = item.Id.ToString(),
                OldValues = before,
                NewValues = Summarize(item),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(item));
    }

    /// <inheritdoc />
    public async Task<Result<InventoryItemDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await _context.Set<InventoryItem>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
            .ConfigureAwait(false);

        return item is null
            ? Result.Failure<InventoryItemDto>(InventoryErrors.NotFound)
            : Result.Success(MapToDto(item));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<InventoryItemDto>>> ListAsync(InventoryItemQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = _context.Set<InventoryItem>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            baseQuery = baseQuery.Where(i => EF.Functions.Like(i.Name, $"%{term}%")
                || (i.Sku != null && EF.Functions.Like(i.Sku, $"%{term}%")));
        }

        if (query.IsActive is { } isActive)
        {
            baseQuery = baseQuery.Where(i => i.IsActive == isActive);
        }

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var items = await baseQuery
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Id)
            .Skip(skip)
            .Take(query.Page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = items.Select(MapToDto).ToList();
        return Result.Success(new PagedResult<InventoryItemDto>(dtos, query.Page.Page, query.Page.PageSize, totalCount));
    }

    private static string Summarize(InventoryItem i) =>
        $"Name={i.Name};Sku={i.Sku};Unit={i.Unit};SupplyLabel={i.SupplyLabel};IsActive={i.IsActive}";

    private static InventoryItemDto MapToDto(InventoryItem i) => new(
        i.Id,
        i.TenantId,
        i.Name,
        i.Sku,
        i.Unit,
        i.SupplyLabel,
        i.IsActive,
        i.Observations,
        i.CreatedAt,
        i.UpdatedAt);
}
