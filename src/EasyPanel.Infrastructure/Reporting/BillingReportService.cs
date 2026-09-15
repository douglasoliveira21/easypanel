using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Reporting;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Reporting;

/// <summary>
/// Implementação de <see cref="IBillingReportService"/> (Fase 9 — R3).
/// Filtra <see cref="Invoice"/> cujo período se sobrepõe ao intervalo
/// solicitado, mesma técnica de sobreposição de vigência já usada em
/// <c>ContractScopeService</c> (Fase 7), agrupando por Cliente.
/// </summary>
public sealed class BillingReportService : IBillingReportService
{
    private readonly AppDbContext _context;

    public BillingReportService(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<BillingReportDto>> GetAsync(BillingReportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = ReportDateRangeValidator.Validate(query.StartDate, query.EndDate);
        if (validation.IsFailure)
        {
            return Result.Failure<BillingReportDto>(validation.Error);
        }

        var startTicks = query.StartDate.UtcTicks;
        var endTicks = query.EndDate.UtcTicks;

        var invoicesQuery = _context.Set<Invoice>().AsNoTracking()
            .Where(i => i.PeriodStartTicks <= endTicks && i.PeriodEndTicks >= startTicks);

        if (query.Status is { } status)
        {
            invoicesQuery = invoicesQuery.Where(i => i.Status == (InvoiceStatus)(int)status);
        }

        var invoices = await invoicesQuery.ToListAsync(ct).ConfigureAwait(false);

        var rows = invoices
            .GroupBy(i => i.CustomerId)
            .Select(g => new BillingReportRowDto(g.Key, g.LongCount(), g.Sum(i => i.TotalAmount)))
            .OrderByDescending(r => r.TotalAmount)
            .ToList();

        return Result.Success(new BillingReportDto(rows, invoices.Count, invoices.Sum(i => i.TotalAmount)));
    }
}
