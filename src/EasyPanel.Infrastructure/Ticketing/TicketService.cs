using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Ticketing;

/// <summary>
/// Implementação de <see cref="ITicketService"/> (Fase 6 — R1/R2/R3/R4). Como
/// as entidades do módulo são <c>TenantEntity</c>, contam com o isolamento
/// automático do ORM; <see cref="ApplicationUser"/> não é, então a validação do
/// responsável atribuído é explícita por <c>TenantId</c> (mesmo padrão de
/// <c>UserService</c>).
/// </summary>
public sealed class TicketService : ITicketService
{
    private const int MaxPageSize = 100;

    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> AllowedTransitions =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.Aberto] = [TicketStatus.EmAndamento, TicketStatus.Cancelado],
            [TicketStatus.EmAndamento] = [TicketStatus.AguardandoCliente, TicketStatus.Resolvido, TicketStatus.Cancelado],
            [TicketStatus.AguardandoCliente] = [TicketStatus.EmAndamento, TicketStatus.Resolvido, TicketStatus.Cancelado],
            [TicketStatus.Resolvido] = [TicketStatus.EmAndamento, TicketStatus.Fechado],
            [TicketStatus.Fechado] = [],
            [TicketStatus.Cancelado] = [],
        };

    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;
    private readonly TicketingOptions _options;

    public TicketService(
        AppDbContext context,
        ITenantContext tenantContext,
        ICurrentUserAccessor currentUser,
        IAuditLogger auditLogger,
        TimeProvider clock,
        IOptions<TicketingOptions> options)
    {
        _context = context;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<Result<TicketDto>> CreateAsync(CreateTicketRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result.Failure<TicketDto>(TicketingErrors.TitleRequired);
        }

        var customerExists = await _context.Set<Customer>().AsNoTracking()
            .AnyAsync(c => c.Id == request.CustomerId, ct).ConfigureAwait(false);
        if (!customerExists)
        {
            return Result.Failure<TicketDto>(TicketingErrors.InvalidCustomer);
        }

        if (request.LocationId is { } locationId
            && !await _context.Set<Location>().AsNoTracking().AnyAsync(l => l.Id == locationId, ct).ConfigureAwait(false))
        {
            return Result.Failure<TicketDto>(TicketingErrors.InvalidLocation);
        }

        if (request.PrinterId is { } printerId
            && !await _context.Set<Printer>().AsNoTracking().AnyAsync(p => p.Id == printerId, ct).ConfigureAwait(false))
        {
            return Result.Failure<TicketDto>(TicketingErrors.InvalidPrinter);
        }

        var slaMinutes = await ResolveSlaAsync(request.Priority, ct).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        var firstResponseDueAt = now.AddMinutes(slaMinutes.FirstResponseMinutes);
        var resolutionDueAt = now.AddMinutes(slaMinutes.ResolutionMinutes);

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = request.Description,
            CustomerId = request.CustomerId,
            LocationId = request.LocationId,
            PrinterId = request.PrinterId,
            Priority = request.Priority,
            Status = TicketStatus.Aberto,
            RequestedByUserId = _currentUser.UserId ?? Guid.Empty,
            FirstResponseDueAt = firstResponseDueAt,
            FirstResponseDueAtTicks = firstResponseDueAt.UtcTicks,
            ResolutionDueAt = resolutionDueAt,
            ResolutionDueAtTicks = resolutionDueAt.UtcTicks,
            CreatedAtTicks = now.UtcTicks,
            CreatedAt = now,
        };

        _context.Set<Ticket>().Add(ticket);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = ticket.TenantId,
                Action = "ticket.create",
                ResourceType = nameof(Ticket),
                ResourceId = ticket.Id.ToString(),
                NewValues = Summarize(ticket),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(ticket));
    }

    /// <inheritdoc />
    public async Task<Result<TicketDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var ticket = await _context.Set<Ticket>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            .ConfigureAwait(false);

        return ticket is null
            ? Result.Failure<TicketDto>(TicketingErrors.NotFound)
            : Result.Success(MapToDto(ticket));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<TicketDto>>> ListAsync(TicketQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = _context.Set<Ticket>().AsNoTracking();

        if (query.Status is { } status)
        {
            baseQuery = baseQuery.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            baseQuery = baseQuery.Where(t => t.Priority == priority);
        }

        if (query.CustomerId is { } customerId)
        {
            baseQuery = baseQuery.Where(t => t.CustomerId == customerId);
        }

        if (query.LocationId is { } locationId)
        {
            baseQuery = baseQuery.Where(t => t.LocationId == locationId);
        }

        if (query.PrinterId is { } printerId)
        {
            baseQuery = baseQuery.Where(t => t.PrinterId == printerId);
        }

        if (query.AssignedToUserId is { } assignedToUserId)
        {
            baseQuery = baseQuery.Where(t => t.AssignedToUserId == assignedToUserId);
        }

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var items = await baseQuery
            .OrderByDescending(t => t.CreatedAtTicks)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(query.Page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = items.Select(MapToDto).ToList();
        return Result.Success(new PagedResult<TicketDto>(dtos, query.Page.Page, query.Page.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<TicketDto>> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await _context.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result.Failure<TicketDto>(TicketingErrors.NotFound);
        }

        if (!AllowedTransitions.TryGetValue(ticket.Status, out var allowed) || !allowed.Contains(request.ToStatus))
        {
            return Result.Failure<TicketDto>(TicketingErrors.InvalidStatusTransition);
        }

        var now = _clock.GetUtcNow();
        var fromStatus = ticket.Status;

        var interaction = new TicketInteraction
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = TicketInteractionType.MudancaStatus,
            ActorUserId = _currentUser.UserId ?? Guid.Empty,
            FromStatus = fromStatus,
            ToStatus = request.ToStatus,
            OccurredAt = now,
            OccurredAtTicks = now.UtcTicks,
            CreatedAt = now,
        };
        _context.Set<TicketInteraction>().Add(interaction);

        ticket.Status = request.ToStatus;
        ticket.UpdatedAt = now;

        if (request.ToStatus == TicketStatus.Resolvido && ticket.ResolvedAt is null)
        {
            ticket.ResolvedAt = now;
            ticket.ResolvedAtTicks = now.UtcTicks;
            ticket.ResolutionCompliance = now.UtcTicks <= ticket.ResolutionDueAtTicks
                ? SlaComplianceStatus.Cumprido
                : SlaComplianceStatus.Violado;
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = ticket.TenantId,
                Action = "ticket.status_change",
                ResourceType = nameof(Ticket),
                ResourceId = ticket.Id.ToString(),
                OldValues = fromStatus.ToString(),
                NewValues = request.ToStatus.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(ticket));
    }

    /// <inheritdoc />
    public async Task<Result<TicketDto>> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await _context.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result.Failure<TicketDto>(TicketingErrors.NotFound);
        }

        if (request.AssignedToUserId is { } assigneeId)
        {
            var tenantId = _tenantContext.TenantId;
            var assigneeExists = await _context.Users.AsNoTracking()
                .AnyAsync(u => u.Id == assigneeId && u.TenantId == tenantId, ct)
                .ConfigureAwait(false);
            if (!assigneeExists)
            {
                return Result.Failure<TicketDto>(TicketingErrors.InvalidAssignee);
            }
        }

        var now = _clock.GetUtcNow();

        var interaction = new TicketInteraction
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = TicketInteractionType.Atribuicao,
            ActorUserId = _currentUser.UserId ?? Guid.Empty,
            AssignedToUserId = request.AssignedToUserId,
            OccurredAt = now,
            OccurredAtTicks = now.UtcTicks,
            CreatedAt = now,
        };
        _context.Set<TicketInteraction>().Add(interaction);

        ticket.AssignedToUserId = request.AssignedToUserId;
        ticket.UpdatedAt = now;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = ticket.TenantId,
                Action = "ticket.assign",
                ResourceType = nameof(Ticket),
                ResourceId = ticket.Id.ToString(),
                NewValues = request.AssignedToUserId?.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(ticket));
    }

    /// <inheritdoc />
    public async Task<Result<TicketInteractionDto>> AddCommentAsync(Guid id, AddTicketCommentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return Result.Failure<TicketInteractionDto>(TicketingErrors.CommentRequired);
        }

        var ticket = await _context.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result.Failure<TicketInteractionDto>(TicketingErrors.NotFound);
        }

        var now = _clock.GetUtcNow();
        var actorId = _currentUser.UserId ?? Guid.Empty;

        var interaction = new TicketInteraction
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = TicketInteractionType.Comentario,
            ActorUserId = actorId,
            Comment = request.Comment.Trim(),
            OccurredAt = now,
            OccurredAtTicks = now.UtcTicks,
            CreatedAt = now,
        };
        _context.Set<TicketInteraction>().Add(interaction);

        if (ticket.FirstResponseAt is null && actorId != ticket.RequestedByUserId)
        {
            ticket.FirstResponseAt = now;
            ticket.FirstResponseAtTicks = now.UtcTicks;
            ticket.FirstResponseCompliance = now.UtcTicks <= ticket.FirstResponseDueAtTicks
                ? SlaComplianceStatus.Cumprido
                : SlaComplianceStatus.Violado;
            ticket.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = ticket.TenantId,
                Action = "ticket.comment",
                ResourceType = nameof(Ticket),
                ResourceId = ticket.Id.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(interaction));
    }

    /// <inheritdoc />
    public async Task<Result<TicketCursorPage<TicketInteractionDto>>> ListInteractionsAsync(
        Guid ticketId, string? cursor, int pageSize, CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        var ticketExists = await _context.Set<Ticket>().AsNoTracking().AnyAsync(t => t.Id == ticketId, ct)
            .ConfigureAwait(false);
        if (!ticketExists)
        {
            return Result.Failure<TicketCursorPage<TicketInteractionDto>>(TicketingErrors.NotFound);
        }

        var q = _context.Set<TicketInteraction>().AsNoTracking().Where(i => i.TicketId == ticketId);

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            q = q.Where(i => i.OccurredAtTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(i => i.OccurredAtTicks)
            .ThenByDescending(i => i.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].OccurredAtTicks);
            rows = rows.Take(size).ToList();
        }

        return Result.Success(new TicketCursorPage<TicketInteractionDto>(rows.Select(MapToDto).ToList(), nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<TicketDto>>> ListSlaBreachedAsync(PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        var nowTicks = _clock.GetUtcNow().UtcTicks;

        var baseQuery = _context.Set<Ticket>().AsNoTracking().Where(t =>
            t.FirstResponseCompliance == SlaComplianceStatus.Violado
            || t.ResolutionCompliance == SlaComplianceStatus.Violado
            || (t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Cancelado
                && ((t.FirstResponseCompliance == SlaComplianceStatus.Pendente && t.FirstResponseDueAtTicks < nowTicks)
                    || (t.ResolutionCompliance == SlaComplianceStatus.Pendente && t.ResolutionDueAtTicks < nowTicks))));

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (page.Page - 1) * page.PageSize;
        var items = await baseQuery
            .OrderByDescending(t => t.CreatedAtTicks)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<TicketDto>(items.Select(MapToDto).ToList(), page.Page, page.PageSize, totalCount));
    }

    private async Task<SlaDefault> ResolveSlaAsync(TicketPriority priority, CancellationToken ct)
    {
        var policy = await _context.Set<SlaPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Priority == priority, ct)
            .ConfigureAwait(false);

        return policy is null
            ? _options.ForPriority(priority)
            : new SlaDefault { FirstResponseMinutes = policy.FirstResponseMinutes, ResolutionMinutes = policy.ResolutionMinutes };
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

    private static string Summarize(Ticket t) =>
        $"Title={t.Title};CustomerId={t.CustomerId};Priority={t.Priority};Status={t.Status}";

    private static TicketDto MapToDto(Ticket t) => new(
        t.Id,
        t.TenantId,
        t.Title,
        t.Description,
        t.CustomerId,
        t.LocationId,
        t.PrinterId,
        t.Priority,
        t.Status,
        t.RequestedByUserId,
        t.AssignedToUserId,
        t.FirstResponseDueAt,
        t.FirstResponseAt,
        t.FirstResponseCompliance,
        t.ResolutionDueAt,
        t.ResolvedAt,
        t.ResolutionCompliance,
        t.CreatedAt);

    private static TicketInteractionDto MapToDto(TicketInteraction i) => new(
        i.Id,
        i.TicketId,
        i.Type,
        i.ActorUserId,
        i.Comment,
        i.FromStatus,
        i.ToStatus,
        i.AssignedToUserId,
        i.OccurredAt);
}
