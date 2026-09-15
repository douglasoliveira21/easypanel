using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Inventory;

/// <summary>
/// Implementação de <see cref="IInventoryMovementService"/> (Fase 5 — R2/R3/R4/R5).
/// Como as entidades do módulo são <c>TenantEntity</c>, contam com o isolamento
/// automático do ORM. <see cref="RegisterAsync"/> valida item/local/impressora,
/// calcula o efeito no saldo conforme o tipo/direção e persiste a movimentação e o
/// saldo materializado numa única <c>SaveChanges</c> (R3.1).
/// </summary>
public sealed class InventoryMovementService : IInventoryMovementService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public InventoryMovementService(
        AppDbContext context,
        ICurrentUserAccessor currentUser,
        IAuditLogger auditLogger,
        TimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<InventoryMovementDto>> RegisterAsync(RegisterInventoryMovementRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Quantity <= 0)
        {
            return Result.Failure<InventoryMovementDto>(InventoryErrors.InvalidQuantity);
        }

        var item = await _context.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == request.ItemId, ct)
            .ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure<InventoryMovementDto>(InventoryErrors.NotFound);
        }

        if (!item.IsActive)
        {
            return Result.Failure<InventoryMovementDto>(InventoryErrors.ItemInactive);
        }

        var locationExists = await _context.Set<Location>().AsNoTracking()
            .AnyAsync(l => l.Id == request.LocationId, ct)
            .ConfigureAwait(false);
        if (!locationExists)
        {
            return Result.Failure<InventoryMovementDto>(InventoryErrors.InvalidLocation);
        }

        if (request.PrinterId is { } printerId
            && !await _context.Set<Printer>().AsNoTracking().AnyAsync(p => p.Id == printerId, ct).ConfigureAwait(false))
        {
            return Result.Failure<InventoryMovementDto>(InventoryErrors.InvalidPrinter);
        }

        if (request.Type == InventoryMovementType.Ajuste
            && (string.IsNullOrWhiteSpace(request.Reason) || request.AdjustmentDirection is null))
        {
            return Result.Failure<InventoryMovementDto>(InventoryErrors.AdjustmentRequiresReason);
        }

        var balance = await _context.Set<InventoryBalance>()
            .FirstOrDefaultAsync(b => b.ItemId == request.ItemId && b.LocationId == request.LocationId, ct)
            .ConfigureAwait(false);

        var currentQuantity = balance?.Quantity ?? 0;
        int newQuantity;

        switch (request.Type)
        {
            case InventoryMovementType.Entrada:
                newQuantity = currentQuantity + request.Quantity;
                break;

            case InventoryMovementType.Saida:
                if (currentQuantity < request.Quantity)
                {
                    return Result.Failure<InventoryMovementDto>(InventoryErrors.InsufficientBalance);
                }

                newQuantity = currentQuantity - request.Quantity;
                break;

            case InventoryMovementType.Ajuste when request.AdjustmentDirection == AdjustmentDirection.Increase:
                newQuantity = currentQuantity + request.Quantity;
                break;

            case InventoryMovementType.Ajuste:
                // Decrease: recalibra para a contagem física real; nunca abaixo de zero.
                newQuantity = Math.Max(0, currentQuantity - request.Quantity);
                break;

            default:
                return Result.Failure<InventoryMovementDto>(InventoryErrors.InvalidQuantity);
        }

        var now = _clock.GetUtcNow();

        if (balance is null)
        {
            balance = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                ItemId = request.ItemId,
                LocationId = request.LocationId,
                Quantity = newQuantity,
                CreatedAt = now,
            };
            _context.Set<InventoryBalance>().Add(balance);
        }
        else
        {
            balance.Quantity = newQuantity;
            balance.UpdatedAt = now;
        }

        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            ItemId = request.ItemId,
            LocationId = request.LocationId,
            Type = request.Type,
            AdjustmentDirection = request.Type == InventoryMovementType.Ajuste ? request.AdjustmentDirection : null,
            Quantity = request.Quantity,
            PrinterId = request.PrinterId,
            Reason = request.Reason,
            ActorUserId = _currentUser.UserId,
            OccurredAt = now,
            OccurredAtTicks = now.UtcTicks,
            CreatedAt = now,
        };
        _context.Set<InventoryMovement>().Add(movement);

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = movement.TenantId,
                Action = "inventorymovement.register",
                ResourceType = nameof(InventoryMovement),
                ResourceId = movement.Id.ToString(),
                OldValues = currentQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValues = newQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(movement));
    }

    /// <inheritdoc />
    public async Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListHistoryAsync(
        Guid itemId,
        Guid? locationId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        var itemExists = await _context.Set<InventoryItem>().AsNoTracking().AnyAsync(i => i.Id == itemId, ct)
            .ConfigureAwait(false);
        if (!itemExists)
        {
            return Result.Failure<InventoryCursorPage<InventoryMovementDto>>(InventoryErrors.NotFound);
        }

        var q = _context.Set<InventoryMovement>().AsNoTracking().Where(m => m.ItemId == itemId);

        if (locationId is { } loc)
        {
            q = q.Where(m => m.LocationId == loc);
        }

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            q = q.Where(m => m.OccurredAtTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(m => m.OccurredAtTicks)
            .ThenByDescending(m => m.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(BuildPage(rows, size));
    }

    /// <inheritdoc />
    public async Task<Result<InventoryCursorPage<InventoryMovementDto>>> ListByPrinterAsync(
        Guid printerId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        var q = _context.Set<InventoryMovement>().AsNoTracking().Where(m => m.PrinterId == printerId);

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            q = q.Where(m => m.OccurredAtTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(m => m.OccurredAtTicks)
            .ThenByDescending(m => m.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(BuildPage(rows, size));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<InventoryBalanceDto>>> GetBalanceByLocationAsync(Guid locationId, CancellationToken ct)
    {
        var locationExists = await _context.Set<Location>().AsNoTracking().AnyAsync(l => l.Id == locationId, ct)
            .ConfigureAwait(false);
        if (!locationExists)
        {
            return Result.Failure<IReadOnlyList<InventoryBalanceDto>>(InventoryErrors.NotFound);
        }

        var balances = await _context.Set<InventoryBalance>()
            .AsNoTracking()
            .Where(b => b.LocationId == locationId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = balances.Select(b => new InventoryBalanceDto(b.ItemId, b.LocationId, b.Quantity)).ToList();
        return Result.Success<IReadOnlyList<InventoryBalanceDto>>(dtos);
    }

    /// <inheritdoc />
    public async Task<Result<InventoryMinimumDto>> SetMinimumAsync(SetInventoryMinimumRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MinimumQuantity < 0)
        {
            return Result.Failure<InventoryMinimumDto>(InventoryErrors.InvalidMinimum);
        }

        var itemExists = await _context.Set<InventoryItem>().AsNoTracking().AnyAsync(i => i.Id == request.ItemId, ct)
            .ConfigureAwait(false);
        if (!itemExists)
        {
            return Result.Failure<InventoryMinimumDto>(InventoryErrors.NotFound);
        }

        var locationExists = await _context.Set<Location>().AsNoTracking().AnyAsync(l => l.Id == request.LocationId, ct)
            .ConfigureAwait(false);
        if (!locationExists)
        {
            return Result.Failure<InventoryMinimumDto>(InventoryErrors.InvalidLocation);
        }

        var existing = await _context.Set<InventoryMinimum>()
            .FirstOrDefaultAsync(m => m.ItemId == request.ItemId && m.LocationId == request.LocationId, ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        int? previous = existing?.MinimumQuantity;
        string action;
        InventoryMinimum minimum;

        if (existing is not null)
        {
            existing.MinimumQuantity = request.MinimumQuantity;
            existing.UpdatedAt = now;
            minimum = existing;
            action = "inventoryminimum.update";
        }
        else
        {
            minimum = new InventoryMinimum
            {
                Id = Guid.NewGuid(),
                ItemId = request.ItemId,
                LocationId = request.LocationId,
                MinimumQuantity = request.MinimumQuantity,
                CreatedAt = now,
            };
            _context.Set<InventoryMinimum>().Add(minimum);
            action = "inventoryminimum.create";
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = minimum.TenantId,
                Action = action,
                ResourceType = nameof(InventoryMinimum),
                ResourceId = minimum.Id.ToString(),
                OldValues = previous?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValues = minimum.MinimumQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(new InventoryMinimumDto(minimum.Id, minimum.ItemId, minimum.LocationId, minimum.MinimumQuantity));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<BelowMinimumDto>>> ListBelowMinimumAsync(PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        var minimums = await _context.Set<InventoryMinimum>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        if (minimums.Count == 0)
        {
            return Result.Success(new PagedResult<BelowMinimumDto>([], page.Page, page.PageSize, 0));
        }

        var balances = await _context.Set<InventoryBalance>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var balanceByKey = balances.ToDictionary(b => (b.ItemId, b.LocationId), b => b.Quantity);

        var below = minimums
            .Select(m => new BelowMinimumDto(
                m.ItemId,
                m.LocationId,
                balanceByKey.GetValueOrDefault((m.ItemId, m.LocationId), 0),
                m.MinimumQuantity))
            .Where(d => d.CurrentQuantity < d.MinimumQuantity)
            .OrderBy(d => d.ItemId)
            .ThenBy(d => d.LocationId)
            .ToList();

        var totalCount = below.Count;
        var skip = (page.Page - 1) * page.PageSize;
        var items = below.Skip(skip).Take(page.PageSize).ToList();

        return Result.Success(new PagedResult<BelowMinimumDto>(items, page.Page, page.PageSize, totalCount));
    }

    private static InventoryCursorPage<InventoryMovementDto> BuildPage(List<InventoryMovement> rows, int size)
    {
        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].OccurredAtTicks);
            rows = rows.Take(size).ToList();
        }

        return new InventoryCursorPage<InventoryMovementDto>(rows.Select(MapToDto).ToList(), nextCursor);
    }

    private static string EncodeCursor(long ticks) => Convert.ToBase64String(BitConverter.GetBytes(ticks));

    private static bool TryDecodeCursor(string? cursor, out long ticks)
    {
        ticks = 0;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            if (bytes.Length != sizeof(long))
            {
                return false;
            }

            ticks = BitConverter.ToInt64(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static InventoryMovementDto MapToDto(InventoryMovement m) => new(
        m.Id,
        m.ItemId,
        m.LocationId,
        m.Type,
        m.AdjustmentDirection,
        m.Quantity,
        m.PrinterId,
        m.Reason,
        m.ActorUserId,
        m.OccurredAt);
}
