using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="ISupplyService"/> (Fase 4 — R3/R4/R5). Como
/// <see cref="SupplyReading"/>/<see cref="SupplyThreshold"/> são <c>TenantEntity</c>,
/// contam com o isolamento automático do ORM (filtro global + interceptor de
/// escrita), no mesmo padrão de <c>CounterService</c>.
///
/// <para><b>Previsão de troca (R5).</b> Regressão linear simples (mínimos
/// quadrados) sobre as últimas <see cref="ForecastSampleSize"/> leituras de um
/// (Impressora, rótulo): <c>x</c> em dias desde a primeira leitura da amostra,
/// <c>y</c> o percentual. Sem inclinação negativa (sem queda observada) ou menos de
/// duas leituras distintas → previsão indisponível (<c>null</c>), sem erro.</para>
/// </summary>
public sealed class SupplyService : ISupplyService
{
    private const int MaxPageSize = 100;
    private const int ForecastSampleSize = 10;

    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _clock;

    public SupplyService(
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
    public async Task<Result<IReadOnlyList<SupplyLevelDto>>> GetCurrentLevelsAsync(Guid printerId, CancellationToken ct)
    {
        var printerExists = await _context.Set<Printer>().AsNoTracking().AnyAsync(p => p.Id == printerId, ct)
            .ConfigureAwait(false);
        if (!printerExists)
        {
            return Result.Failure<IReadOnlyList<SupplyLevelDto>>(SupplyErrors.NotFound);
        }

        var labels = await _context.Set<SupplyReading>()
            .AsNoTracking()
            .Where(r => r.PrinterId == printerId)
            .Select(r => r.Label)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var levels = new List<SupplyLevelDto>();
        foreach (var label in labels)
        {
            var latest = await _context.Set<SupplyReading>()
                .AsNoTracking()
                .Where(r => r.PrinterId == printerId && r.Label == label)
                .OrderByDescending(r => r.TimestampTicks)
                .FirstAsync(ct)
                .ConfigureAwait(false);

            var forecast = await ForecastDepletionAsync(printerId, label, ct).ConfigureAwait(false);
            levels.Add(new SupplyLevelDto(printerId, label, latest.Percent, latest.Timestamp, forecast));
        }

        return Result.Success<IReadOnlyList<SupplyLevelDto>>(levels);
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<SupplyReadingDto>>> ListHistoryAsync(
        Guid printerId,
        string? label,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        var printerExists = await _context.Set<Printer>().AsNoTracking().AnyAsync(p => p.Id == printerId, ct)
            .ConfigureAwait(false);
        if (!printerExists)
        {
            return Result.Failure<CursorPage<SupplyReadingDto>>(SupplyErrors.NotFound);
        }

        var q = _context.Set<SupplyReading>().AsNoTracking().Where(r => r.PrinterId == printerId);

        if (!string.IsNullOrWhiteSpace(label))
        {
            q = q.Where(r => r.Label == label);
        }

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            q = q.Where(r => r.TimestampTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(r => r.TimestampTicks)
            .ThenByDescending(r => r.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].TimestampTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(MapReadingToDto).ToList();
        return Result.Success(new CursorPage<SupplyReadingDto>(items, nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<SupplyThresholdDto>>> ListThresholdsAsync(
        SupplyThresholdQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = _context.Set<SupplyThreshold>().AsNoTracking();
        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var rows = await baseQuery
            .OrderBy(t => t.Id)
            .Skip(skip)
            .Take(query.Page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = rows.Select(MapThresholdToDto).ToList();
        return Result.Success(new PagedResult<SupplyThresholdDto>(items, query.Page.Page, query.Page.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<SupplyThresholdDto>> SetThresholdAsync(SetSupplyThresholdRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ThresholdPercent is < 0 or > 100)
        {
            return Result.Failure<SupplyThresholdDto>(SupplyErrors.InvalidThresholdPercent);
        }

        if (request.PrinterId is { } printerId
            && !await _context.Set<Printer>().AsNoTracking().AnyAsync(p => p.Id == printerId, ct).ConfigureAwait(false))
        {
            return Result.Failure<SupplyThresholdDto>(SupplyErrors.NotFound);
        }

        var existing = await _context.Set<SupplyThreshold>()
            .FirstOrDefaultAsync(t => t.PrinterId == request.PrinterId && t.Label == request.Label, ct)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        string action;
        SupplyThreshold threshold;
        int? previousPercent = null;

        if (existing is not null)
        {
            previousPercent = existing.ThresholdPercent;
            existing.ThresholdPercent = request.ThresholdPercent;
            existing.UpdatedAt = now;
            threshold = existing;
            action = "supplythreshold.update";
        }
        else
        {
            threshold = new SupplyThreshold
            {
                Id = Guid.NewGuid(),
                PrinterId = request.PrinterId,
                Label = request.Label,
                ThresholdPercent = request.ThresholdPercent,
                CreatedAt = now,
            };
            _context.Set<SupplyThreshold>().Add(threshold);
            action = "supplythreshold.create";
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = threshold.TenantId,
                Action = action,
                ResourceType = nameof(SupplyThreshold),
                ResourceId = threshold.Id.ToString(),
                OldValues = previousPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValues = threshold.ThresholdPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapThresholdToDto(threshold));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteThresholdAsync(Guid id, CancellationToken ct)
    {
        var threshold = await _context.Set<SupplyThreshold>().FirstOrDefaultAsync(t => t.Id == id, ct)
            .ConfigureAwait(false);
        if (threshold is null)
        {
            return Result.Failure(SupplyErrors.NotFound);
        }

        var tenantId = threshold.TenantId;
        var resourceId = threshold.Id.ToString();
        var previousPercent = threshold.ThresholdPercent;

        _context.Set<SupplyThreshold>().Remove(threshold);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = tenantId,
                Action = "supplythreshold.delete",
                ResourceType = nameof(SupplyThreshold),
                ResourceId = resourceId,
                OldValues = previousPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success();
    }

    private async Task<DateTimeOffset?> ForecastDepletionAsync(Guid printerId, string label, CancellationToken ct)
    {
        var sample = await _context.Set<SupplyReading>()
            .AsNoTracking()
            .Where(r => r.PrinterId == printerId && r.Label == label)
            .OrderByDescending(r => r.TimestampTicks)
            .Take(ForecastSampleSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (sample.Count < 2)
        {
            return null;
        }

        var ordered = sample.OrderBy(r => r.TimestampTicks).ToList();
        var t0 = ordered[0].TimestampTicks;

        // x em dias desde a primeira leitura da amostra; y = percentual.
        var points = ordered
            .Select(r => (X: (r.TimestampTicks - t0) / (double)TimeSpan.TicksPerDay, Y: (double)r.Percent))
            .ToList();

        if (points.Select(p => p.X).Distinct().Count() < 2)
        {
            // Todas as leituras no mesmo instante (ou instantes indistinguíveis em dias): sem taxa calculável.
            return null;
        }

        var n = points.Count;
        var sumX = points.Sum(p => p.X);
        var sumY = points.Sum(p => p.Y);
        var sumXY = points.Sum(p => p.X * p.Y);
        var sumXX = points.Sum(p => p.X * p.X);

        var denominator = (n * sumXX) - (sumX * sumX);
        if (denominator == 0)
        {
            return null;
        }

        var slope = ((n * sumXY) - (sumX * sumY)) / denominator; // percentual/dia

        if (slope >= 0)
        {
            // Sem queda observada (nível estável ou subindo, ex.: troca recente).
            return null;
        }

        var latest = ordered[^1];
        var daysUntilDepletion = latest.Percent / -slope;

        return latest.Timestamp.AddDays(daysUntilDepletion);
    }

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

    private static SupplyReadingDto MapReadingToDto(SupplyReading r) => new(r.Id, r.PrinterId, r.Label, r.Percent, r.Timestamp);

    private static SupplyThresholdDto MapThresholdToDto(SupplyThreshold t) => new(t.Id, t.PrinterId, t.Label, t.ThresholdPercent);
}
