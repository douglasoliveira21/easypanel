using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Ticketing;

/// <summary>
/// Implementação de <see cref="ISlaPolicyService"/> (Fase 6 — R4.1/R4.2/R4.7):
/// upsert por (tenant, prioridade), auditado.
/// </summary>
public sealed class SlaPolicyService : ISlaPolicyService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public SlaPolicyService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger, TimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SlaPolicyDto>>> ListAsync(CancellationToken ct)
    {
        var policies = await _context.Set<SlaPolicy>().AsNoTracking()
            .OrderBy(p => p.Priority)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<SlaPolicyDto>>(policies.Select(MapToDto).ToList());
    }

    /// <inheritdoc />
    public async Task<Result<SlaPolicyDto>> SetAsync(SetSlaPolicyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.FirstResponseMinutes <= 0
            || request.ResolutionMinutes <= 0
            || request.ResolutionMinutes < request.FirstResponseMinutes)
        {
            return Result.Failure<SlaPolicyDto>(TicketingErrors.InvalidSlaPolicy);
        }

        var existing = await _context.Set<SlaPolicy>()
            .FirstOrDefaultAsync(p => p.Priority == request.Priority, ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        string? previous = existing is null ? null : Summarize(existing);
        SlaPolicy policy;
        string action;

        if (existing is not null)
        {
            existing.FirstResponseMinutes = request.FirstResponseMinutes;
            existing.ResolutionMinutes = request.ResolutionMinutes;
            existing.UpdatedAt = now;
            policy = existing;
            action = "slapolicy.update";
        }
        else
        {
            policy = new SlaPolicy
            {
                Id = Guid.NewGuid(),
                Priority = request.Priority,
                FirstResponseMinutes = request.FirstResponseMinutes,
                ResolutionMinutes = request.ResolutionMinutes,
                CreatedAt = now,
            };
            _context.Set<SlaPolicy>().Add(policy);
            action = "slapolicy.create";
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = policy.TenantId,
                Action = action,
                ResourceType = nameof(SlaPolicy),
                ResourceId = policy.Id.ToString(),
                OldValues = previous,
                NewValues = Summarize(policy),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(policy));
    }

    private static string Summarize(SlaPolicy p) =>
        $"Priority={p.Priority};FirstResponseMinutes={p.FirstResponseMinutes};ResolutionMinutes={p.ResolutionMinutes}";

    private static SlaPolicyDto MapToDto(SlaPolicy p) => new(p.Id, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes);
}
