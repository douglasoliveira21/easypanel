using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Implementação de <see cref="IAlertSilenceService"/> (R7). Como
/// <see cref="AlertSilence"/> é uma <c>TenantEntity</c>, conta com o isolamento
/// automático do ORM (filtro global de consulta + interceptor de escrita).
/// </summary>
public sealed class AlertSilenceService : IAlertSilenceService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public AlertSilenceService(
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
    public async Task<Result<AlertSilenceDto>> CreateAsync(CreateAlertSilenceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AlertRuleId is null && request.PrinterId is null && request.WindowsClientId is null)
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.SilenceScopeRequired);
        }

        if (request.EndsAt <= request.StartsAt)
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.InvalidSilencePeriod);
        }

        if (request.AlertRuleId is { } ruleId
            && !await _context.Set<AlertRule>().AsNoTracking().AnyAsync(r => r.Id == ruleId, ct).ConfigureAwait(false))
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.InvalidScope);
        }

        if (request.PrinterId is { } printerId
            && !await _context.Set<Printer>().AsNoTracking().AnyAsync(p => p.Id == printerId, ct).ConfigureAwait(false))
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.InvalidScope);
        }

        if (request.WindowsClientId is { } clientId
            && !await _context.Set<WindowsClient>().AsNoTracking().AnyAsync(c => c.Id == clientId, ct).ConfigureAwait(false))
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.InvalidScope);
        }

        var actorId = _currentUser.UserId ?? Guid.Empty;
        var silence = new AlertSilence
        {
            Id = Guid.NewGuid(),
            AlertRuleId = request.AlertRuleId,
            PrinterId = request.PrinterId,
            WindowsClientId = request.WindowsClientId,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            CreatedByUserId = actorId,
            Reason = request.Reason,
            CreatedAt = _clock.GetUtcNow(),
        };

        _context.Set<AlertSilence>().Add(silence);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = silence.TenantId,
                Action = "alertsilence.create",
                ResourceType = nameof(AlertSilence),
                ResourceId = silence.Id.ToString(),
                NewValues = Summarize(silence),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(silence));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<AlertSilenceDto>>> ListAsync(AlertSilenceQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Filtro/ordenação por DateTimeOffset não são traduzidos pelo provider
        // SQLite dos testes (nota do projeto); como o volume de silenciamentos por
        // tenant é pequeno (diferente de Alert/PrinterEvent), aplica-se em memória.
        var all = await _context.Set<AlertSilence>()
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        IEnumerable<AlertSilence> filtered = all;
        if (query.ActiveOnly == true)
        {
            var now = _clock.GetUtcNow();
            filtered = filtered.Where(s => s.StartsAt <= now && s.EndsAt > now && s.EndedEarlyAt == null);
        }

        var ordered = filtered
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .ToList();

        var totalCount = ordered.Count;
        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var items = ordered.Skip(skip).Take(query.Page.PageSize).Select(MapToDto).ToList();

        return Result.Success(new PagedResult<AlertSilenceDto>(items, query.Page.Page, query.Page.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<AlertSilenceDto>> EndEarlyAsync(Guid id, CancellationToken ct)
    {
        var silence = await _context.Set<AlertSilence>().FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (silence is null)
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.NotFound);
        }

        if (silence.EndedEarlyAt is not null)
        {
            return Result.Failure<AlertSilenceDto>(AlertingErrors.SilenceAlreadyEnded);
        }

        var now = _clock.GetUtcNow();
        silence.EndedEarlyAt = now;
        silence.EndedEarlyByUserId = _currentUser.UserId;
        silence.UpdatedAt = now;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = silence.TenantId,
                Action = "alertsilence.end",
                ResourceType = nameof(AlertSilence),
                ResourceId = silence.Id.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(silence));
    }

    private static string Summarize(AlertSilence s) =>
        $"AlertRuleId={s.AlertRuleId};PrinterId={s.PrinterId};WindowsClientId={s.WindowsClientId};" +
        $"StartsAt={s.StartsAt:o};EndsAt={s.EndsAt:o}";

    private static AlertSilenceDto MapToDto(AlertSilence s) => new(
        s.Id,
        s.TenantId,
        s.AlertRuleId,
        s.PrinterId,
        s.WindowsClientId,
        s.StartsAt,
        s.EndsAt,
        s.CreatedByUserId,
        s.Reason,
        s.EndedEarlyAt,
        s.EndedEarlyByUserId,
        s.CreatedAt);
}
