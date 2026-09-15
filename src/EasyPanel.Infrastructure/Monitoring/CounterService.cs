using System.Globalization;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="ICounterService"/> (R11.4–R11.7).
///
/// Leituras são somente-adição em <see cref="PrinterCounter"/>. O registro valida
/// não-decréscimo por (impressora, tipo) (R11.5): uma leitura inferior ao maior
/// valor já registrado é recusada com <see cref="MonitoringErrors.CounterDecrease"/>
/// (409). O ajuste administrativo (R11.6) pode reduzir o valor, exige justificativa
/// e é auditado (ator, valor anterior/novo, justificativa). A consulta de histórico
/// usa cursor pagination por <c>TimestampTicks</c> (portável), restrita ao tenant.
/// </summary>
public sealed class CounterService : ICounterService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;

    public CounterService(
        AppDbContext context,
        ICurrentUserAccessor currentUser,
        IAuditLogger auditLogger)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    /// <inheritdoc />
    public async Task<Result<PrinterCounterDto>> RecordAsync(
        Guid printerId,
        CounterType counterType,
        string? counterTypeLabel,
        long value,
        CounterSource source,
        Guid? windowsClientId,
        Guid? collectionId,
        CancellationToken ct)
    {
        // A impressora deve pertencer ao tenant corrente (auto-escopada) → 404.
        var printerExists = await _context.Set<Printer>()
            .AnyAsync(p => p.Id == printerId, ct)
            .ConfigureAwait(false);
        if (!printerExists)
        {
            return Result.Failure<PrinterCounterDto>(MonitoringErrors.NotFound);
        }

        var max = await _context.Set<PrinterCounter>()
            .Where(c => c.PrinterId == printerId && c.CounterType == counterType)
            .Select(c => (long?)c.Value)
            .MaxAsync(ct)
            .ConfigureAwait(false);

        // Não-decréscimo (R11.5): leitura inferior ao último valor é recusada.
        if (max is { } m && value < m)
        {
            return Result.Failure<PrinterCounterDto>(MonitoringErrors.CounterDecrease);
        }

        var now = DateTimeOffset.UtcNow;
        var counter = new PrinterCounter
        {
            Id = Guid.NewGuid(),
            PrinterId = printerId,
            Timestamp = now,
            TimestampTicks = now.UtcTicks,
            CounterType = counterType,
            CounterTypeLabel = counterType == CounterType.Other ? counterTypeLabel : null,
            Value = value,
            Source = source,
            WindowsClientId = windowsClientId,
            CollectionId = collectionId,
            IsAdministrativeAdjustment = false,
            CreatedAt = now,
        };

        _context.Set<PrinterCounter>().Add(counter);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success(MapToDto(counter));
    }

    /// <inheritdoc />
    public async Task<Result<PrinterCounterDto>> AdjustAsync(
        CounterAdjustmentRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Justification))
        {
            return Result.Failure<PrinterCounterDto>(MonitoringErrors.AdjustmentJustificationRequired);
        }

        var printer = await _context.Set<Printer>()
            .FirstOrDefaultAsync(p => p.Id == request.PrinterId, ct)
            .ConfigureAwait(false);
        if (printer is null)
        {
            return Result.Failure<PrinterCounterDto>(MonitoringErrors.NotFound);
        }

        var previous = await _context.Set<PrinterCounter>()
            .Where(c => c.PrinterId == request.PrinterId && c.CounterType == request.CounterType)
            .Select(c => (long?)c.Value)
            .MaxAsync(ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var counter = new PrinterCounter
        {
            Id = Guid.NewGuid(),
            PrinterId = request.PrinterId,
            Timestamp = now,
            TimestampTicks = now.UtcTicks,
            CounterType = request.CounterType,
            CounterTypeLabel = request.CounterType == CounterType.Other ? request.CounterTypeLabel : null,
            Value = request.NewValue,
            Source = CounterSource.Manual,
            IsAdministrativeAdjustment = true,
            CreatedAt = now,
        };

        _context.Set<PrinterCounter>().Add(counter);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        // Auditoria do ajuste (R11.6): ator, valor anterior/novo, justificativa.
        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = printer.TenantId,
                Action = "counter.adjust",
                ResourceType = nameof(PrinterCounter),
                ResourceId = counter.Id.ToString(),
                OldValues = previous is { } p
                    ? p.ToString(CultureInfo.InvariantCulture)
                    : null,
                NewValues = request.NewValue.ToString(CultureInfo.InvariantCulture),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(counter));
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<PrinterCounterDto>>> ListAsync(
        Guid printerId,
        CounterType? counterType,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        var printerExists = await _context.Set<Printer>()
            .AnyAsync(p => p.Id == printerId, ct)
            .ConfigureAwait(false);
        if (!printerExists)
        {
            return Result.Failure<CursorPage<PrinterCounterDto>>(MonitoringErrors.NotFound);
        }

        var q = _context.Set<PrinterCounter>()
            .AsNoTracking()
            .Where(c => c.PrinterId == printerId);

        if (counterType is { } type)
        {
            q = q.Where(c => c.CounterType == type);
        }

        // Cursor = ticks do último item da página anterior; página mais recente
        // primeiro (ticks decrescente). Portável (ordena por long).
        if (TryDecodeCursor(cursor, out var cursorTicks))
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
            var last = rows[size - 1];
            nextCursor = EncodeCursor(last.TimestampTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(MapToDto).ToList();
        return Result.Success(new CursorPage<PrinterCounterDto>(items, nextCursor));
    }

    private static string EncodeCursor(long ticks) =>
        Convert.ToBase64String(BitConverter.GetBytes(ticks));

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

    private static PrinterCounterDto MapToDto(PrinterCounter c) => new(
        c.Id,
        c.PrinterId,
        c.Timestamp,
        c.CounterType,
        c.CounterTypeLabel,
        c.Value,
        c.Source,
        c.IsAdministrativeAdjustment);
}
