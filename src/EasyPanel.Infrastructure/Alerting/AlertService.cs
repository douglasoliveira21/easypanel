using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Implementação de <see cref="IAlertService"/> (R3/R6). Como <see cref="Alert"/> é
/// uma <c>TenantEntity</c>, conta com o isolamento automático do ORM (filtro global
/// de consulta + interceptor de escrita). Cursor pagination por
/// <c>LastOccurrenceAtTicks</c>/<c>AttemptedAtTicks</c> (portável), no mesmo padrão
/// de <c>CounterService</c>.
/// </summary>
public sealed class AlertService : IAlertService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public AlertService(
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
    public async Task<Result<AlertCursorPage<AlertDto>>> ListAsync(AlertQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = query.PageSize is <= 0 or > MaxPageSize ? MaxPageSize : query.PageSize;

        var baseQuery = _context.Set<Alert>().AsNoTracking();

        if (query.State is { } state)
        {
            baseQuery = baseQuery.Where(a => a.State == state);
        }

        if (query.Severity is { } severity)
        {
            baseQuery = baseQuery.Where(a => a.Severity == severity);
        }

        if (query.AlertRuleId is { } ruleId)
        {
            baseQuery = baseQuery.Where(a => a.AlertRuleId == ruleId);
        }

        if (query.PrinterId is { } printerId)
        {
            baseQuery = baseQuery.Where(a => a.PrinterId == printerId);
        }

        if (query.WindowsClientId is { } clientId)
        {
            baseQuery = baseQuery.Where(a => a.WindowsClientId == clientId);
        }

        if (query.From is { } from)
        {
            baseQuery = baseQuery.Where(a => a.LastOccurrenceAt >= from);
        }

        if (query.To is { } to)
        {
            baseQuery = baseQuery.Where(a => a.LastOccurrenceAt <= to);
        }

        if (TryDecodeCursor(query.Cursor, out var cursorTicks))
        {
            baseQuery = baseQuery.Where(a => a.LastOccurrenceAtTicks < cursorTicks);
        }

        var rows = await baseQuery
            .OrderByDescending(a => a.LastOccurrenceAtTicks)
            .ThenByDescending(a => a.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].LastOccurrenceAtTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(a => MapToDto(a, null)).ToList();
        return Result.Success(new AlertCursorPage<AlertDto>(items, nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result<AlertDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var alert = await _context.Set<Alert>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            .ConfigureAwait(false);
        if (alert is null)
        {
            return Result.Failure<AlertDto>(AlertingErrors.NotFound);
        }

        // Ordenação em memória: SQLite (testes) não traduz ORDER BY sobre
        // DateTimeOffset; o volume por Alerta é pequeno.
        var transitions = (await _context.Set<AlertTransition>()
            .AsNoTracking()
            .Where(t => t.AlertId == id)
            .ToListAsync(ct)
            .ConfigureAwait(false))
            .OrderBy(t => t.OccurredAt)
            .ThenBy(t => t.Id)
            .ToList();

        return Result.Success(MapToDto(alert, transitions.Select(MapTransitionToDto).ToList()));
    }

    /// <inheritdoc />
    public async Task<Result<AlertDto>> AcknowledgeAsync(Guid id, CancellationToken ct)
    {
        var alert = await _context.Set<Alert>().FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        if (alert is null)
        {
            return Result.Failure<AlertDto>(AlertingErrors.NotFound);
        }

        if (alert.State != AlertState.Open)
        {
            return Result.Failure<AlertDto>(AlertingErrors.InvalidAlertTransition);
        }

        var now = _clock.GetUtcNow();
        var previousState = alert.State;
        alert.State = AlertState.Acknowledged;
        alert.AcknowledgedAt = now;
        alert.AcknowledgedByUserId = _currentUser.UserId;
        alert.UpdatedAt = now;

        _context.Set<AlertTransition>().Add(new AlertTransition
        {
            Id = Guid.NewGuid(),
            AlertId = alert.Id,
            FromState = previousState,
            ToState = AlertState.Acknowledged,
            ActorUserId = _currentUser.UserId,
            OccurredAt = now,
            CreatedAt = now,
        });

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = alert.TenantId,
                Action = "alert.acknowledge",
                ResourceType = nameof(Alert),
                ResourceId = alert.Id.ToString(),
                OldValues = previousState.ToString(),
                NewValues = alert.State.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(alert, null));
    }

    /// <inheritdoc />
    public async Task<Result<AlertDto>> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alert = await _context.Set<Alert>().FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        if (alert is null)
        {
            return Result.Failure<AlertDto>(AlertingErrors.NotFound);
        }

        if (alert.State is not (AlertState.Open or AlertState.Acknowledged))
        {
            return Result.Failure<AlertDto>(AlertingErrors.InvalidAlertTransition);
        }

        var now = _clock.GetUtcNow();
        var previousState = alert.State;
        alert.State = AlertState.Resolved;
        alert.ResolvedAt = now;
        alert.ResolvedByUserId = _currentUser.UserId;
        alert.AutoResolved = false;
        alert.ResolutionNote = request.Note;
        alert.UpdatedAt = now;

        _context.Set<AlertTransition>().Add(new AlertTransition
        {
            Id = Guid.NewGuid(),
            AlertId = alert.Id,
            FromState = previousState,
            ToState = AlertState.Resolved,
            ActorUserId = _currentUser.UserId,
            OccurredAt = now,
            Note = request.Note,
            CreatedAt = now,
        });

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = alert.TenantId,
                Action = "alert.resolve",
                ResourceType = nameof(Alert),
                ResourceId = alert.Id.ToString(),
                OldValues = previousState.ToString(),
                NewValues = alert.State.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(alert, null));
    }

    /// <inheritdoc />
    public async Task<Result<AlertCursorPage<AlertNotificationAttemptDto>>> ListNotificationsAsync(
        Guid alertId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        var alertExists = await _context.Set<Alert>().AsNoTracking().AnyAsync(a => a.Id == alertId, ct)
            .ConfigureAwait(false);
        if (!alertExists)
        {
            return Result.Failure<AlertCursorPage<AlertNotificationAttemptDto>>(AlertingErrors.NotFound);
        }

        var baseQuery = _context.Set<AlertNotificationAttempt>()
            .AsNoTracking()
            .Where(a => a.AlertId == alertId);

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            baseQuery = baseQuery.Where(a => a.AttemptedAtTicks < cursorTicks);
        }

        var rows = await baseQuery
            .OrderByDescending(a => a.AttemptedAtTicks)
            .ThenByDescending(a => a.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].AttemptedAtTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(MapAttemptToDto).ToList();
        return Result.Success(new AlertCursorPage<AlertNotificationAttemptDto>(items, nextCursor));
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

    private static AlertTransitionDto MapTransitionToDto(AlertTransition t) => new(
        t.Id,
        t.FromState,
        t.ToState,
        t.ActorUserId,
        t.OccurredAt,
        t.Note);

    private static AlertNotificationAttemptDto MapAttemptToDto(AlertNotificationAttempt a) => new(
        a.Id,
        a.AlertId,
        a.Channel,
        a.Outcome,
        a.AttemptNumber,
        a.HttpStatusCode,
        a.ErrorSummary,
        a.AttemptedAt);

    private static AlertDto MapToDto(Alert a, IReadOnlyList<AlertTransitionDto>? transitions) => new(
        a.Id,
        a.TenantId,
        a.AlertRuleId,
        a.Severity,
        a.State,
        a.PrinterId,
        a.WindowsClientId,
        a.FirstOccurrenceAt,
        a.LastOccurrenceAt,
        a.OccurrenceCount,
        a.AcknowledgedAt,
        a.AcknowledgedByUserId,
        a.ResolvedAt,
        a.ResolvedByUserId,
        a.AutoResolved,
        a.ResolutionNote,
        transitions);
}
