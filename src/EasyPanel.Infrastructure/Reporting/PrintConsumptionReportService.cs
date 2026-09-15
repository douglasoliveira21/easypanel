using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Reporting;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Reporting;

/// <summary>
/// Implementação de <see cref="IPrintConsumptionReportService"/> (Fase 9 —
/// R2). Reimplementa a técnica de cálculo de consumo por período da Fase 8
/// (<c>BillingClosingService.CalculateConsumptionAsync</c>) — leitura de
/// referência de fim menos início do intervalo, por (Impressora,
/// CounterType), nunca negativa — sem depender de <c>Modules.Billing</c>.
/// </summary>
public sealed class PrintConsumptionReportService : IPrintConsumptionReportService
{
    private static readonly ReportingCounterType[] AllCounterTypes =
    [
        ReportingCounterType.BlackAndWhite,
        ReportingCounterType.Color,
        ReportingCounterType.A3,
        ReportingCounterType.A4,
        ReportingCounterType.Scan,
        ReportingCounterType.Other,
    ];

    private readonly AppDbContext _context;

    public PrintConsumptionReportService(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<PrintConsumptionReportDto>> GetAsync(PrintConsumptionReportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = ReportDateRangeValidator.Validate(query.StartDate, query.EndDate);
        if (validation.IsFailure)
        {
            return Result.Failure<PrintConsumptionReportDto>(validation.Error);
        }

        var startTicks = query.StartDate.UtcTicks;
        var endTicks = query.EndDate.UtcTicks;

        var printersQuery = _context.Set<Printer>().AsNoTracking();
        if (query.CustomerId is { } customerId)
        {
            printersQuery = printersQuery.Where(p => p.CustomerId == customerId);
        }

        if (query.LocationId is { } locationId)
        {
            printersQuery = printersQuery.Where(p => p.LocationId == locationId);
        }

        var printers = await printersQuery.ToListAsync(ct).ConfigureAwait(false);

        var rows = new List<PrintConsumptionRowDto>();
        foreach (var printer in printers)
        {
            foreach (var counterType in AllCounterTypes)
            {
                var consumed = await CalculateConsumptionAsync(printer.Id, counterType, startTicks, endTicks, ct)
                    .ConfigureAwait(false);
                if (consumed is null)
                {
                    continue;
                }

                rows.Add(new PrintConsumptionRowDto(printer.CustomerId, printer.LocationId, printer.Id, counterType, consumed.Value));
            }
        }

        return Result.Success(new PrintConsumptionReportDto(rows, query.StartDate, query.EndDate));
    }

    private async Task<long?> CalculateConsumptionAsync(
        Guid printerId, ReportingCounterType counterType, long startTicks, long endTicks, CancellationToken ct)
    {
        var monitoringType = (CounterType)(int)counterType;

        var endReading = await _context.Set<PrinterCounter>().AsNoTracking()
            .Where(c => c.PrinterId == printerId && c.CounterType == monitoringType && c.TimestampTicks <= endTicks)
            .OrderByDescending(c => c.TimestampTicks)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (endReading is null)
        {
            return null;
        }

        var startReading = await _context.Set<PrinterCounter>().AsNoTracking()
            .Where(c => c.PrinterId == printerId && c.CounterType == monitoringType && c.TimestampTicks <= startTicks)
            .OrderByDescending(c => c.TimestampTicks)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        startReading ??= await _context.Set<PrinterCounter>().AsNoTracking()
            .Where(c => c.PrinterId == printerId && c.CounterType == monitoringType
                && c.TimestampTicks > startTicks && c.TimestampTicks <= endTicks)
            .OrderBy(c => c.TimestampTicks)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (startReading is null)
        {
            return null;
        }

        return Math.Max(0, endReading.Value - startReading.Value);
    }
}
