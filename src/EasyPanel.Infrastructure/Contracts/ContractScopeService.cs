using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Contracts;

/// <summary>
/// Implementação de <see cref="IContractScopeService"/> (Fase 7 — R2). Valida
/// que o Local/Impressora pertence ao mesmo Cliente do Contrato (R2.3) e que
/// não há sobreposição de vigência com outro contrato não-encerrado do mesmo
/// Cliente já vinculado ao mesmo Local/Impressora (R2.4) — necessário para a
/// resolução determinística de R4.
/// </summary>
public sealed class ContractScopeService : IContractScopeService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public ContractScopeService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger, TimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result> AddLocationAsync(Guid contractId, Guid locationId, CancellationToken ct)
    {
        var contract = await _context.Set<Contract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(ContractErrors.NotFound);
        }

        var location = await _context.Set<Location>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct)
            .ConfigureAwait(false);
        if (location is null || location.CustomerId != contract.CustomerId)
        {
            return Result.Failure(ContractErrors.ScopeCustomerMismatch);
        }

        var overlaps = await (
            from cl in _context.Set<ContractLocation>().AsNoTracking()
            join c in _context.Set<Contract>().AsNoTracking() on cl.ContractId equals c.Id
            where cl.LocationId == locationId
                && c.CustomerId == contract.CustomerId
                && c.Id != contractId
                && c.Status != ContractStatus.Encerrado
                && c.StartDateTicks <= (contract.EndDateTicks ?? long.MaxValue)
                && (c.EndDateTicks == null || c.EndDateTicks >= contract.StartDateTicks)
            select cl.Id)
            .AnyAsync(ct)
            .ConfigureAwait(false);
        if (overlaps)
        {
            return Result.Failure(ContractErrors.ScopeOverlap);
        }

        var now = _clock.GetUtcNow();
        var contractLocation = new ContractLocation
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            LocationId = locationId,
            CreatedAt = now,
        };
        _context.Set<ContractLocation>().Add(contractLocation);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditScopeAsync("contract.scope_add", contract.TenantId, contractId, "Location", locationId, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemoveLocationAsync(Guid contractId, Guid locationId, CancellationToken ct)
    {
        var contract = await _context.Set<Contract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(ContractErrors.NotFound);
        }

        var link = await _context.Set<ContractLocation>()
            .FirstOrDefaultAsync(cl => cl.ContractId == contractId && cl.LocationId == locationId, ct)
            .ConfigureAwait(false);
        if (link is null)
        {
            return Result.Failure(ContractErrors.NotFound);
        }

        _context.Set<ContractLocation>().Remove(link);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditScopeAsync("contract.scope_remove", contract.TenantId, contractId, "Location", locationId, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Guid>>> ListLocationsAsync(Guid contractId, CancellationToken ct)
    {
        var contractExists = await _context.Set<Contract>().AsNoTracking().AnyAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (!contractExists)
        {
            return Result.Failure<IReadOnlyList<Guid>>(ContractErrors.NotFound);
        }

        var locationIds = await _context.Set<ContractLocation>().AsNoTracking()
            .Where(cl => cl.ContractId == contractId)
            .Select(cl => cl.LocationId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<Guid>>(locationIds);
    }

    /// <inheritdoc />
    public async Task<Result> AddPrinterAsync(Guid contractId, Guid printerId, CancellationToken ct)
    {
        var contract = await _context.Set<Contract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(ContractErrors.NotFound);
        }

        var printer = await _context.Set<Printer>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == printerId, ct)
            .ConfigureAwait(false);
        if (printer is null || printer.CustomerId != contract.CustomerId)
        {
            return Result.Failure(ContractErrors.ScopeCustomerMismatch);
        }

        var overlaps = await (
            from cp in _context.Set<ContractPrinter>().AsNoTracking()
            join c in _context.Set<Contract>().AsNoTracking() on cp.ContractId equals c.Id
            where cp.PrinterId == printerId
                && c.CustomerId == contract.CustomerId
                && c.Id != contractId
                && c.Status != ContractStatus.Encerrado
                && c.StartDateTicks <= (contract.EndDateTicks ?? long.MaxValue)
                && (c.EndDateTicks == null || c.EndDateTicks >= contract.StartDateTicks)
            select cp.Id)
            .AnyAsync(ct)
            .ConfigureAwait(false);
        if (overlaps)
        {
            return Result.Failure(ContractErrors.ScopeOverlap);
        }

        var now = _clock.GetUtcNow();
        var contractPrinter = new ContractPrinter
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            PrinterId = printerId,
            CreatedAt = now,
        };
        _context.Set<ContractPrinter>().Add(contractPrinter);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditScopeAsync("contract.scope_add", contract.TenantId, contractId, "Printer", printerId, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RemovePrinterAsync(Guid contractId, Guid printerId, CancellationToken ct)
    {
        var contract = await _context.Set<Contract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure(ContractErrors.NotFound);
        }

        var link = await _context.Set<ContractPrinter>()
            .FirstOrDefaultAsync(cp => cp.ContractId == contractId && cp.PrinterId == printerId, ct)
            .ConfigureAwait(false);
        if (link is null)
        {
            return Result.Failure(ContractErrors.NotFound);
        }

        _context.Set<ContractPrinter>().Remove(link);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditScopeAsync("contract.scope_remove", contract.TenantId, contractId, "Printer", printerId, ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Guid>>> ListPrintersAsync(Guid contractId, CancellationToken ct)
    {
        var contractExists = await _context.Set<Contract>().AsNoTracking().AnyAsync(c => c.Id == contractId, ct)
            .ConfigureAwait(false);
        if (!contractExists)
        {
            return Result.Failure<IReadOnlyList<Guid>>(ContractErrors.NotFound);
        }

        var printerIds = await _context.Set<ContractPrinter>().AsNoTracking()
            .Where(cp => cp.ContractId == contractId)
            .Select(cp => cp.PrinterId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<Guid>>(printerIds);
    }

    private Task AuditScopeAsync(string action, Guid tenantId, Guid contractId, string scopeType, Guid scopeId, CancellationToken ct) =>
        _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = tenantId,
                Action = action,
                ResourceType = nameof(Contract),
                ResourceId = contractId.ToString(),
                NewValues = $"ScopeType={scopeType};ScopeId={scopeId}",
                Result = AuditResult.Success,
            },
            ct);
}
