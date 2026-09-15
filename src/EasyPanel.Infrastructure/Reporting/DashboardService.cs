using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Reporting;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Reporting;

/// <summary>
/// Implementação de <see cref="IDashboardService"/> (Fase 9 — R1). Cruza
/// <c>Modules.Reporting</c> com <c>Modules.Monitoring</c>,
/// <c>Modules.Alerting</c>, <c>Modules.Ticketing</c>, <c>Modules.Inventory</c>
/// e <c>Modules.Billing</c> — todos consultados ao vivo, sem cache (decisão
/// da Fase 9, ver <c>requirements.md</c>).
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly TimeProvider _clock;

    public DashboardService(AppDbContext context, IInventoryMovementService inventoryMovementService, TimeProvider clock)
    {
        _context = context;
        _inventoryMovementService = inventoryMovementService;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<DashboardOverviewDto>> GetOverviewAsync(CancellationToken ct)
    {
        var printersByStatus = await CountByAsync<Printer, PrinterStatus>(p => p.Status, ct).ConfigureAwait(false);

        var alertsByState = await CountByAsync<Alert, AlertState>(a => a.State, ct).ConfigureAwait(false);

        var openAlertsBySeverity = await _context.Set<Alert>().AsNoTracking()
            .Where(a => a.State != AlertState.Resolved)
            .GroupBy(a => a.Severity)
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct)
            .ConfigureAwait(false);

        var ticketsByStatus = await CountByAsync<Ticket, TicketStatus>(t => t.Status, ct).ConfigureAwait(false);

        var invoicesByStatus = await CountByAsync<Invoice, InvoiceStatus>(i => i.Status, ct).ConfigureAwait(false);

        var invoicesPendingTotal = await _context.Set<Invoice>().AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Rascunho || i.Status == InvoiceStatus.Emitida)
            .SumAsync(i => (decimal?)i.TotalAmount, ct)
            .ConfigureAwait(false) ?? 0m;

        var belowMinimum = await _inventoryMovementService
            .ListBelowMinimumAsync(new PageRequest(1, 1), ct)
            .ConfigureAwait(false);
        var lowStockItemCount = belowMinimum.IsSuccess ? belowMinimum.Value.TotalCount : 0;

        var overview = new DashboardOverviewDto(
            ToStringKeyed<PrinterStatus>(printersByStatus),
            ToStringKeyed<AlertState>(alertsByState),
            ToStringKeyed<AlertSeverity>(openAlertsBySeverity),
            ToStringKeyed<TicketStatus>(ticketsByStatus),
            lowStockItemCount,
            ToStringKeyed<InvoiceStatus>(invoicesByStatus),
            invoicesPendingTotal,
            _clock.GetUtcNow());

        return Result.Success(overview);
    }

    private async Task<Dictionary<TEnum, long>> CountByAsync<TEntity, TEnum>(
        System.Linq.Expressions.Expression<Func<TEntity, TEnum>> selector, CancellationToken ct)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var grouped = await _context.Set<TEntity>().AsNoTracking()
            .GroupBy(selector)
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return grouped.ToDictionary(x => x.Key, x => x.Count);
    }

    private static IReadOnlyDictionary<string, long> ToStringKeyed<TEnum>(IReadOnlyDictionary<TEnum, long> counts)
        where TEnum : struct, Enum
    {
        var result = new Dictionary<string, long>();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            result[value.ToString()] = counts.TryGetValue(value, out var count) ? count : 0;
        }

        return result;
    }
}
