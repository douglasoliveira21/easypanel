using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Contracts;

/// <summary>
/// Implementação de <see cref="IContractFranchiseService"/> (Fase 7 — R3):
/// upsert por (Contrato, CounterType), auditado.
/// </summary>
public sealed class ContractFranchiseService : IContractFranchiseService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public ContractFranchiseService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger, TimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<ContractFranchiseDto>> SetAsync(Guid contractId, SetContractFranchiseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IncludedQuantity < 0 || request.ExcessUnitPrice < 0)
        {
            return Result.Failure<ContractFranchiseDto>(ContractErrors.InvalidFranchise);
        }

        var contract = await _context.Set<Contract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure<ContractFranchiseDto>(ContractErrors.NotFound);
        }

        var existing = await _context.Set<ContractFranchise>()
            .FirstOrDefaultAsync(f => f.ContractId == contractId && f.CounterType == request.CounterType, ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        string? previous = existing is null ? null : Summarize(existing);
        ContractFranchise franchise;
        string action;

        if (existing is not null)
        {
            existing.CounterTypeLabel = request.CounterTypeLabel;
            existing.IncludedQuantity = request.IncludedQuantity;
            existing.ExcessUnitPrice = request.ExcessUnitPrice;
            existing.UpdatedAt = now;
            franchise = existing;
            action = "contractfranchise.update";
        }
        else
        {
            franchise = new ContractFranchise
            {
                Id = Guid.NewGuid(),
                ContractId = contractId,
                CounterType = request.CounterType,
                CounterTypeLabel = request.CounterTypeLabel,
                IncludedQuantity = request.IncludedQuantity,
                ExcessUnitPrice = request.ExcessUnitPrice,
                CreatedAt = now,
            };
            _context.Set<ContractFranchise>().Add(franchise);
            action = "contractfranchise.create";
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = franchise.TenantId,
                Action = action,
                ResourceType = nameof(ContractFranchise),
                ResourceId = franchise.Id.ToString(),
                OldValues = previous,
                NewValues = Summarize(franchise),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(franchise));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContractFranchiseDto>>> ListAsync(Guid contractId, CancellationToken ct)
    {
        var contractExists = await _context.Set<Contract>().AsNoTracking().AnyAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (!contractExists)
        {
            return Result.Failure<IReadOnlyList<ContractFranchiseDto>>(ContractErrors.NotFound);
        }

        var franchises = await _context.Set<ContractFranchise>().AsNoTracking()
            .Where(f => f.ContractId == contractId)
            .OrderBy(f => f.CounterType)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ContractFranchiseDto>>(franchises.Select(MapToDto).ToList());
    }

    private static string Summarize(ContractFranchise f) =>
        $"CounterType={f.CounterType};IncludedQuantity={f.IncludedQuantity};ExcessUnitPrice={f.ExcessUnitPrice};Currency={f.Currency}";

    private static ContractFranchiseDto MapToDto(ContractFranchise f) => new(
        f.Id, f.ContractId, f.CounterType, f.CounterTypeLabel, f.IncludedQuantity, f.ExcessUnitPrice, f.Currency);
}
