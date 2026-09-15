using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Contracts;

/// <summary>
/// Implementação de <see cref="IContractService"/> (Fase 7 — R1/R4/R5). Como
/// as entidades do módulo são <c>TenantEntity</c>, contam com o isolamento
/// automático do ORM; a resolução do contrato aplicável (R4) cruza
/// <c>Modules.Contracts</c> com <c>Modules.Monitoring</c> (Impressora), mesmo
/// padrão já usado pelo <c>AlertEngine</c>/`TicketService`.
/// </summary>
public sealed class ContractService : IContractService
{
    private static readonly IReadOnlyDictionary<ContractStatus, ContractStatus[]> AllowedTransitions =
        new Dictionary<ContractStatus, ContractStatus[]>
        {
            [ContractStatus.Rascunho] = [ContractStatus.Ativo, ContractStatus.Encerrado],
            [ContractStatus.Ativo] = [ContractStatus.Suspenso, ContractStatus.Encerrado],
            [ContractStatus.Suspenso] = [ContractStatus.Ativo, ContractStatus.Encerrado],
            [ContractStatus.Encerrado] = [],
        };

    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public ContractService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger, TimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Number))
        {
            return Result.Failure<ContractDto>(ContractErrors.NumberRequired);
        }

        var customerExists = await _context.Set<Customer>().AsNoTracking()
            .AnyAsync(c => c.Id == request.CustomerId, ct).ConfigureAwait(false);
        if (!customerExists)
        {
            return Result.Failure<ContractDto>(ContractErrors.InvalidCustomer);
        }

        if (request.EndDate is { } endDate && endDate < request.StartDate)
        {
            return Result.Failure<ContractDto>(ContractErrors.InvalidDateRange);
        }

        var now = _clock.GetUtcNow();
        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            Number = request.Number.Trim(),
            CustomerId = request.CustomerId,
            StartDate = request.StartDate,
            StartDateTicks = request.StartDate.UtcTicks,
            EndDate = request.EndDate,
            EndDateTicks = request.EndDate?.UtcTicks,
            Status = ContractStatus.Rascunho,
            Observations = request.Observations,
            CreatedAt = now,
            CreatedAtTicks = now.UtcTicks,
        };

        _context.Set<Contract>().Add(contract);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = contract.TenantId,
                Action = "contract.create",
                ResourceType = nameof(Contract),
                ResourceId = contract.Id.ToString(),
                NewValues = Summarize(contract),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(contract, now));
    }

    /// <inheritdoc />
    public async Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Number))
        {
            return Result.Failure<ContractDto>(ContractErrors.NumberRequired);
        }

        var contract = await _context.Set<Contract>().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure<ContractDto>(ContractErrors.NotFound);
        }

        if (request.EndDate is { } endDate && endDate < contract.StartDate)
        {
            return Result.Failure<ContractDto>(ContractErrors.InvalidDateRange);
        }

        var before = Summarize(contract);

        contract.Number = request.Number.Trim();
        contract.EndDate = request.EndDate;
        contract.EndDateTicks = request.EndDate?.UtcTicks;
        contract.Observations = request.Observations;
        var now = _clock.GetUtcNow();
        contract.UpdatedAt = now;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = contract.TenantId,
                Action = "contract.update",
                ResourceType = nameof(Contract),
                ResourceId = contract.Id.ToString(),
                OldValues = before,
                NewValues = Summarize(contract),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(contract, now));
    }

    /// <inheritdoc />
    public async Task<Result<ContractDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var contract = await _context.Set<Contract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

        return contract is null
            ? Result.Failure<ContractDto>(ContractErrors.NotFound)
            : Result.Success(MapToDto(contract, _clock.GetUtcNow()));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ContractDto>>> ListAsync(ContractQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = _context.Set<Contract>().AsNoTracking();

        if (query.CustomerId is { } customerId)
        {
            baseQuery = baseQuery.Where(c => c.CustomerId == customerId);
        }

        if (query.Status is { } status)
        {
            baseQuery = baseQuery.Where(c => c.Status == status);
        }

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var items = await baseQuery
            .OrderByDescending(c => c.CreatedAtTicks)
            .ThenByDescending(c => c.Id)
            .Skip(skip)
            .Take(query.Page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var dtos = items.Select(c => MapToDto(c, now)).ToList();
        return Result.Success(new PagedResult<ContractDto>(dtos, query.Page.Page, query.Page.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contract = await _context.Set<Contract>().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Result.Failure<ContractDto>(ContractErrors.NotFound);
        }

        if (!AllowedTransitions.TryGetValue(contract.Status, out var allowed) || !allowed.Contains(request.ToStatus))
        {
            return Result.Failure<ContractDto>(ContractErrors.InvalidStatusTransition);
        }

        var fromStatus = contract.Status;
        contract.Status = request.ToStatus;
        var now = _clock.GetUtcNow();
        contract.UpdatedAt = now;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = contract.TenantId,
                Action = "contract.status_change",
                ResourceType = nameof(Contract),
                ResourceId = contract.Id.ToString(),
                OldValues = fromStatus.ToString(),
                NewValues = request.ToStatus.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(contract, now));
    }

    /// <inheritdoc />
    public async Task<Result<ContractDto?>> ResolveApplicableAsync(Guid printerId, DateTimeOffset referenceDate, CancellationToken ct)
    {
        var printer = await _context.Set<Printer>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == printerId, ct)
            .ConfigureAwait(false);
        if (printer is null)
        {
            return Result.Success<ContractDto?>(null);
        }

        var referenceTicks = referenceDate.UtcTicks;
        var vigentQuery = _context.Set<Contract>().AsNoTracking()
            .Where(c => c.CustomerId == printer.CustomerId
                && c.Status == ContractStatus.Ativo
                && c.StartDateTicks <= referenceTicks
                && (c.EndDateTicks == null || c.EndDateTicks >= referenceTicks));

        var byPrinter = await (
            from c in vigentQuery
            join cp in _context.Set<ContractPrinter>().AsNoTracking() on c.Id equals cp.ContractId
            where cp.PrinterId == printerId
            select c)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (byPrinter is not null)
        {
            return Result.Success<ContractDto?>(MapToDto(byPrinter, referenceDate));
        }

        var byLocation = await (
            from c in vigentQuery
            join cl in _context.Set<ContractLocation>().AsNoTracking() on c.Id equals cl.ContractId
            where cl.LocationId == printer.LocationId
            select c)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (byLocation is not null)
        {
            return Result.Success<ContractDto?>(MapToDto(byLocation, referenceDate));
        }

        var contractIdsWithScope = await _context.Set<ContractLocation>().AsNoTracking().Select(cl => cl.ContractId)
            .Concat(_context.Set<ContractPrinter>().AsNoTracking().Select(cp => cp.ContractId))
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var unscoped = await vigentQuery
            .Where(c => !contractIdsWithScope.Contains(c.Id))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return Result.Success<ContractDto?>(unscoped is null ? null : MapToDto(unscoped, referenceDate));
    }

    private static string Summarize(Contract c) =>
        $"Number={c.Number};CustomerId={c.CustomerId};StartDate={c.StartDate:O};EndDate={c.EndDate:O};Status={c.Status}";

    private static ContractDto MapToDto(Contract c, DateTimeOffset now) => new(
        c.Id,
        c.TenantId,
        c.Number,
        c.CustomerId,
        c.StartDate,
        c.EndDate,
        c.Status,
        c.Observations,
        c.EndDate is { } end && end < now,
        c.CreatedAt,
        c.UpdatedAt);
}
