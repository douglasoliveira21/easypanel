using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Billing;

/// <summary>
/// Implementação de <see cref="IInvoiceService"/> (Fase 8 — R5/R6). Como as
/// entidades do módulo são <c>TenantEntity</c>, contam com o isolamento
/// automático do ORM.
/// </summary>
public sealed class InvoiceService : IInvoiceService
{
    private static readonly IReadOnlyDictionary<InvoiceStatus, InvoiceStatus[]> AllowedTransitions =
        new Dictionary<InvoiceStatus, InvoiceStatus[]>
        {
            [InvoiceStatus.Rascunho] = [InvoiceStatus.Emitida, InvoiceStatus.Cancelada],
            [InvoiceStatus.Emitida] = [InvoiceStatus.Cancelada],
            [InvoiceStatus.Cancelada] = [],
        };

    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public InvoiceService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger, TimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var invoice = await _context.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
            .ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<InvoiceDto>(BillingErrors.NotFound);
        }

        var items = await _context.Set<InvoiceLineItem>().AsNoTracking()
            .Where(li => li.InvoiceId == id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(invoice, items));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<InvoiceDto>>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = _context.Set<Invoice>().AsNoTracking();

        if (query.CustomerId is { } customerId)
        {
            baseQuery = baseQuery.Where(i => i.CustomerId == customerId);
        }

        if (query.ContractId is { } contractId)
        {
            baseQuery = baseQuery.Where(i => i.ContractId == contractId);
        }

        if (query.Status is { } status)
        {
            baseQuery = baseQuery.Where(i => i.Status == status);
        }

        // Filtra por ticks (não por DateTimeOffset.Year/Month — SQLite não
        // traduz comparação/extração de membro sobre DateTimeOffset de forma
        // confiável; PeriodStartTicks já é o cursor portável).
        if (query.Year is { } year && query.Month is { } month)
        {
            var periodStartTicks = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
            baseQuery = baseQuery.Where(i => i.PeriodStartTicks == periodStartTicks);
        }
        else if (query.Year is { } yearOnly)
        {
            var yearStartTicks = new DateTimeOffset(yearOnly, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
            var yearEndTicks = new DateTimeOffset(yearOnly + 1, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
            baseQuery = baseQuery.Where(i => i.PeriodStartTicks >= yearStartTicks && i.PeriodStartTicks < yearEndTicks);
        }

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var invoices = await baseQuery
            .OrderByDescending(i => i.GeneratedAtTicks)
            .ThenByDescending(i => i.Id)
            .Skip(skip)
            .Take(query.Page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = invoices.Select(i => MapToDto(i, [])).ToList();
        return Result.Success(new PagedResult<InvoiceDto>(dtos, query.Page.Page, query.Page.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceDto>> ChangeStatusAsync(Guid id, ChangeInvoiceStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoice = await _context.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == id, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<InvoiceDto>(BillingErrors.NotFound);
        }

        if (!AllowedTransitions.TryGetValue(invoice.Status, out var allowed) || !allowed.Contains(request.ToStatus))
        {
            return Result.Failure<InvoiceDto>(BillingErrors.InvalidStatusTransition);
        }

        var fromStatus = invoice.Status;
        var now = _clock.GetUtcNow();
        invoice.Status = request.ToStatus;
        invoice.UpdatedAt = now;

        if (request.ToStatus == InvoiceStatus.Emitida)
        {
            invoice.IssuedAt = now;
        }
        else if (request.ToStatus == InvoiceStatus.Cancelada)
        {
            invoice.CancelledAt = now;
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = invoice.TenantId,
                Action = "invoice.status_change",
                ResourceType = nameof(Invoice),
                ResourceId = invoice.Id.ToString(),
                OldValues = fromStatus.ToString(),
                NewValues = request.ToStatus.ToString(),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        var items = await _context.Set<InvoiceLineItem>().AsNoTracking()
            .Where(li => li.InvoiceId == id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(invoice, items));
    }

    private static InvoiceDto MapToDto(Invoice i, IReadOnlyList<InvoiceLineItem> items) => new(
        i.Id,
        i.ContractId,
        i.CustomerId,
        i.PeriodStart,
        i.PeriodEnd,
        i.Status,
        i.TotalAmount,
        i.Currency,
        i.GeneratedAt,
        i.IssuedAt,
        i.CancelledAt,
        items.Select(MapToDto).ToList());

    private static InvoiceLineItemDto MapToDto(InvoiceLineItem li) => new(
        li.Id,
        li.InvoiceId,
        li.PrinterId,
        li.CounterType,
        li.CounterTypeLabel,
        li.ConsumedQuantity,
        li.IncludedQuantity,
        li.ExcessQuantity,
        li.UnitPrice,
        li.LineAmount);
}
