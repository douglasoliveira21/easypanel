using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Billing;

/// <summary>
/// Implementação de <see cref="IBillingClosingService"/> (Fase 8 —
/// R1/R2/R3/R4). Cruza <c>Modules.Billing</c> com <c>Modules.Monitoring</c>
/// (<see cref="PrinterCounter"/>) e <c>Modules.Contracts</c>
/// (<see cref="IContractService.ResolveApplicableAsync"/>,
/// <see cref="ContractFranchise"/>), mesmo padrão do <c>AlertEngine</c>/
/// <c>TicketService</c>.
/// </summary>
public sealed class BillingClosingService : IBillingClosingService
{
    private static readonly BillingCounterType[] AllCounterTypes =
    [
        BillingCounterType.BlackAndWhite,
        BillingCounterType.Color,
        BillingCounterType.A3,
        BillingCounterType.A4,
        BillingCounterType.Scan,
        BillingCounterType.Other,
    ];

    private readonly AppDbContext _context;
    private readonly IContractService _contractService;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public BillingClosingService(
        AppDbContext context,
        IContractService contractService,
        ICurrentUserAccessor currentUser,
        IAuditLogger auditLogger,
        TimeProvider clock)
    {
        _context = context;
        _contractService = contractService;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<BillingClosingDto>> ExecuteAsync(ExecuteBillingClosingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Month is < 1 or > 12)
        {
            return Result.Failure<BillingClosingDto>(BillingErrors.InvalidMonth);
        }

        var periodStart = new DateTimeOffset(request.Year, request.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = periodStart.AddMonths(1).AddSeconds(-1);
        var now = _clock.GetUtcNow();

        if (now < periodEnd)
        {
            return Result.Failure<BillingClosingDto>(BillingErrors.PeriodNotClosed);
        }

        var alreadyClosed = await _context.Set<BillingClosing>().AsNoTracking()
            .AnyAsync(c => c.Year == request.Year && c.Month == request.Month, ct)
            .ConfigureAwait(false);
        if (alreadyClosed)
        {
            return Result.Failure<BillingClosingDto>(BillingErrors.AlreadyClosed);
        }

        var periodStartTicks = periodStart.UtcTicks;
        var periodEndTicks = periodEnd.UtcTicks;

        var printers = await _context.Set<Printer>().AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var itemsByContract = new Dictionary<Guid, List<InvoiceLineItem>>();
        var customerByContract = new Dictionary<Guid, Guid>();

        foreach (var printer in printers)
        {
            var contractResult = await _contractService
                .ResolveApplicableAsync(printer.Id, periodEnd, ct)
                .ConfigureAwait(false);
            if (contractResult.IsFailure || contractResult.Value is null)
            {
                continue;
            }

            var contract = contractResult.Value;

            foreach (var counterType in AllCounterTypes)
            {
                var consumption = await CalculateConsumptionAsync(
                    printer.Id, counterType, periodStartTicks, periodEndTicks, ct).ConfigureAwait(false);
                if (consumption is null)
                {
                    continue;
                }

                var franchise = await _context.Set<ContractFranchise>().AsNoTracking()
                    .FirstOrDefaultAsync(
                        f => f.ContractId == contract.Id && f.CounterType == (ContractCounterType)(int)counterType, ct)
                    .ConfigureAwait(false);
                if (franchise is null)
                {
                    continue;
                }

                var excess = Math.Max(0, consumption.Value - franchise.IncludedQuantity);
                if (excess <= 0)
                {
                    continue;
                }

                var lineAmount = Math.Round(excess * franchise.ExcessUnitPrice, 2, MidpointRounding.ToEven);

                var item = new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = Guid.Empty, // preenchido após a Fatura ser criada
                    PrinterId = printer.Id,
                    CounterType = counterType,
                    CounterTypeLabel = franchise.CounterTypeLabel,
                    ConsumedQuantity = consumption.Value,
                    IncludedQuantity = franchise.IncludedQuantity,
                    ExcessQuantity = excess,
                    UnitPrice = franchise.ExcessUnitPrice,
                    LineAmount = lineAmount,
                    CreatedAt = now,
                };

                if (!itemsByContract.TryGetValue(contract.Id, out var list))
                {
                    list = [];
                    itemsByContract[contract.Id] = list;
                    customerByContract[contract.Id] = contract.CustomerId;
                }

                list.Add(item);
            }
        }

        var invoiceCount = 0;
        foreach (var (contractId, items) in itemsByContract)
        {
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                ContractId = contractId,
                CustomerId = customerByContract[contractId],
                PeriodStart = periodStart,
                PeriodStartTicks = periodStartTicks,
                PeriodEnd = periodEnd,
                PeriodEndTicks = periodEndTicks,
                Status = InvoiceStatus.Rascunho,
                TotalAmount = items.Sum(i => i.LineAmount),
                GeneratedAt = now,
                GeneratedAtTicks = now.UtcTicks,
                CreatedAt = now,
            };
            _context.Set<Invoice>().Add(invoice);

            foreach (var item in items)
            {
                item.InvoiceId = invoice.Id;
                _context.Set<InvoiceLineItem>().Add(item);
            }

            invoiceCount++;
        }

        var closing = new BillingClosing
        {
            Id = Guid.NewGuid(),
            Year = request.Year,
            Month = request.Month,
            PeriodStart = periodStart,
            PeriodStartTicks = periodStartTicks,
            PeriodEnd = periodEnd,
            PeriodEndTicks = periodEndTicks,
            ExecutedAt = now,
            ExecutedAtTicks = now.UtcTicks,
            ExecutedByUserId = _currentUser.UserId,
            InvoiceCount = invoiceCount,
            CreatedAt = now,
        };
        _context.Set<BillingClosing>().Add(closing);

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = closing.TenantId,
                Action = "billingclosing.execute",
                ResourceType = nameof(BillingClosing),
                ResourceId = closing.Id.ToString(),
                NewValues = $"Year={request.Year};Month={request.Month};InvoiceCount={invoiceCount}",
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(closing));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<BillingClosingDto>>> ListAsync(PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        var baseQuery = _context.Set<BillingClosing>().AsNoTracking();

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (page.Page - 1) * page.PageSize;
        var items = await baseQuery
            .OrderByDescending(c => c.ExecutedAtTicks)
            .ThenByDescending(c => c.Id)
            .Skip(skip)
            .Take(page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(new PagedResult<BillingClosingDto>(items.Select(MapToDto).ToList(), page.Page, page.PageSize, totalCount));
    }

    /// <summary>
    /// Calcula o consumo de um (Impressora, CounterType) no período (R2).
    /// Retorna <c>null</c> quando não há nenhuma leitura de referência
    /// disponível (nem antes, nem dentro do período — R2.5).
    /// </summary>
    private async Task<long?> CalculateConsumptionAsync(
        Guid printerId, BillingCounterType counterType, long periodStartTicks, long periodEndTicks, CancellationToken ct)
    {
        var monitoringType = (CounterType)(int)counterType;

        var endReading = await _context.Set<PrinterCounter>().AsNoTracking()
            .Where(c => c.PrinterId == printerId && c.CounterType == monitoringType && c.TimestampTicks <= periodEndTicks)
            .OrderByDescending(c => c.TimestampTicks)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (endReading is null)
        {
            return null;
        }

        var startReading = await _context.Set<PrinterCounter>().AsNoTracking()
            .Where(c => c.PrinterId == printerId && c.CounterType == monitoringType && c.TimestampTicks <= periodStartTicks)
            .OrderByDescending(c => c.TimestampTicks)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        startReading ??= await _context.Set<PrinterCounter>().AsNoTracking()
            .Where(c => c.PrinterId == printerId && c.CounterType == monitoringType
                && c.TimestampTicks > periodStartTicks && c.TimestampTicks <= periodEndTicks)
            .OrderBy(c => c.TimestampTicks)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (startReading is null)
        {
            return null;
        }

        return Math.Max(0, endReading.Value - startReading.Value);
    }

    private static BillingClosingDto MapToDto(BillingClosing c) => new(
        c.Id, c.Year, c.Month, c.PeriodStart, c.PeriodEnd, c.ExecutedAt, c.ExecutedByUserId, c.InvoiceCount);
}
