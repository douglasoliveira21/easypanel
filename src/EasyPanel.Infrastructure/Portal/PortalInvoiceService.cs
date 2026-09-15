using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Portal;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Portal;

/// <summary>
/// Implementação de <see cref="IPortalInvoiceService"/> (Fase 10 — R5). Restringe
/// toda consulta ao Cliente do <see cref="ICustomerContext"/>, além do filtro
/// global de tenant já aplicado a <see cref="Invoice"/> (herdado), e exclui
/// sempre Faturas em <see cref="InvoiceStatus.Rascunho"/> (R5.2).
/// </summary>
public sealed class PortalInvoiceService : IPortalInvoiceService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ICustomerContext _customerContext;

    public PortalInvoiceService(AppDbContext context, ICustomerContext customerContext)
    {
        _context = context;
        _customerContext = customerContext;
    }

    /// <inheritdoc />
    public async Task<Result<PortalCursorPage<PortalInvoiceSummaryDto>>> ListAsync(
        string? cursor, int pageSize, CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        if (_customerContext.CustomerId is not { } customerId)
        {
            return Result.Success(new PortalCursorPage<PortalInvoiceSummaryDto>(Array.Empty<PortalInvoiceSummaryDto>(), null));
        }

        var q = _context.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.Status != InvoiceStatus.Rascunho);

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            q = q.Where(i => i.PeriodStartTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(i => i.PeriodStartTicks)
            .ThenByDescending(i => i.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].PeriodStartTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(ToSummaryDto).ToList();

        return Result.Success(new PortalCursorPage<PortalInvoiceSummaryDto>(items, nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result<PortalInvoiceDetailDto>> GetAsync(Guid invoiceId, CancellationToken ct)
    {
        if (_customerContext.CustomerId is not { } customerId)
        {
            return Result.Failure<PortalInvoiceDetailDto>(PortalErrors.NotFound);
        }

        // Fatura de outro Cliente, inexistente ou ainda em Rascunho: 404
        // uniforme, sem distinguir os casos (R5.2/R5.3, não-enumeração).
        var invoice = await _context.Invoices.AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.Id == invoiceId && i.CustomerId == customerId && i.Status != InvoiceStatus.Rascunho, ct)
            .ConfigureAwait(false);
        if (invoice is null)
        {
            return Result.Failure<PortalInvoiceDetailDto>(PortalErrors.NotFound);
        }

        var lineItems = await _context.InvoiceLineItems.AsNoTracking()
            .Where(li => li.InvoiceId == invoiceId)
            .OrderBy(li => li.Id)
            .Select(li => new PortalInvoiceLineItemDto(
                li.PrinterId,
                li.CounterType.ToString(),
                li.ConsumedQuantity,
                li.IncludedQuantity,
                li.ExcessQuantity,
                li.UnitPrice,
                li.LineAmount))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(new PortalInvoiceDetailDto(
            invoice.Id,
            invoice.PeriodStart,
            invoice.PeriodEnd,
            (PortalInvoiceStatus)(int)invoice.Status,
            invoice.TotalAmount,
            lineItems));
    }

    private static PortalInvoiceSummaryDto ToSummaryDto(Invoice invoice) => new(
        invoice.Id, invoice.PeriodStart, invoice.PeriodEnd, (PortalInvoiceStatus)(int)invoice.Status, invoice.TotalAmount);

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
}
