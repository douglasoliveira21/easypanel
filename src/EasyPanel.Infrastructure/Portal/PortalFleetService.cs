using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Portal;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Portal;

/// <summary>
/// Implementação de <see cref="IPortalFleetService"/> (Fase 10 — R3). Restringe
/// toda consulta ao Cliente do <see cref="ICustomerContext"/>, além do filtro
/// global de tenant já aplicado a <see cref="Printer"/>/<see cref="PrinterCounter"/>
/// (herdado, <see cref="Shared.Kernel.Entities.TenantEntity"/>).
/// </summary>
public sealed class PortalFleetService : IPortalFleetService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ICustomerContext _customerContext;

    public PortalFleetService(AppDbContext context, ICustomerContext customerContext)
    {
        _context = context;
        _customerContext = customerContext;
    }

    /// <inheritdoc />
    public async Task<Result<PortalCursorPage<PortalPrinterDto>>> ListPrintersAsync(
        string? cursor, int pageSize, CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        if (_customerContext.CustomerId is not { } customerId)
        {
            return Result.Success(new PortalCursorPage<PortalPrinterDto>(Array.Empty<PortalPrinterDto>(), null));
        }

        var q =
            from printer in _context.Printers.AsNoTracking()
            join location in _context.Locations.AsNoTracking() on printer.LocationId equals location.Id
            where printer.CustomerId == customerId
            select new { printer, location.Nome };

        if (TryDecodeCursor(cursor, out var cursorId))
        {
            q = q.Where(x => x.printer.Id.CompareTo(cursorId) > 0);
        }

        var rows = await q
            .OrderBy(x => x.printer.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].printer.Id);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(x => new PortalPrinterDto(
            x.printer.Id,
            x.printer.Fabricante,
            x.printer.Modelo,
            x.printer.NumeroSerie,
            x.printer.LocationId,
            x.Nome,
            (PortalPrinterStatus)(int)x.printer.Status)).ToList();

        return Result.Success(new PortalCursorPage<PortalPrinterDto>(items, nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result<PortalCursorPage<PortalCounterReadingDto>>> GetCounterHistoryAsync(
        Guid printerId, string? cursor, int pageSize, CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        if (_customerContext.CustomerId is not { } customerId)
        {
            return Result.Failure<PortalCursorPage<PortalCounterReadingDto>>(PortalErrors.NotFound);
        }

        // Impressora de outro Cliente (mesmo tenant) ou inexistente: 404 uniforme,
        // sem distinguir os dois casos (R3.2, não-enumeração).
        var printerBelongsToCustomer = await _context.Printers.AsNoTracking()
            .AnyAsync(p => p.Id == printerId && p.CustomerId == customerId, ct)
            .ConfigureAwait(false);
        if (!printerBelongsToCustomer)
        {
            return Result.Failure<PortalCursorPage<PortalCounterReadingDto>>(PortalErrors.NotFound);
        }

        var q = _context.PrinterCounters.AsNoTracking().Where(c => c.PrinterId == printerId);

        if (TryDecodeTicksCursor(cursor, out var cursorTicks))
        {
            q = q.Where(c => c.TimestampTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(c => c.TimestampTicks)
            .ThenByDescending(c => c.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeTicksCursor(rows[size - 1].TimestampTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows
            .Select(c => new PortalCounterReadingDto(c.Timestamp, c.CounterType.ToString(), c.Value))
            .ToList();

        return Result.Success(new PortalCursorPage<PortalCounterReadingDto>(items, nextCursor));
    }

    private static string EncodeCursor(Guid id) => Convert.ToBase64String(id.ToByteArray());

    private static bool TryDecodeCursor(string? cursor, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            if (bytes.Length != 16)
            {
                return false;
            }

            id = new Guid(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string EncodeTicksCursor(long ticks) => Convert.ToBase64String(BitConverter.GetBytes(ticks));

    private static bool TryDecodeTicksCursor(string? cursor, out long ticks)
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
