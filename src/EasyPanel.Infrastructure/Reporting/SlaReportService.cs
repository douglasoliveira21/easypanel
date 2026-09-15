using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Reporting;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Reporting;

/// <summary>
/// Implementação de <see cref="ISlaReportService"/> (Fase 9 — R4). Agrega os
/// indicadores de cumprimento de SLA já existentes em <see cref="Ticket"/>
/// (Fase 6), filtrando por data de abertura (<c>CreatedAtTicks</c>).
/// </summary>
public sealed class SlaReportService : ISlaReportService
{
    private readonly AppDbContext _context;

    public SlaReportService(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<SlaReportDto>> GetAsync(SlaReportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = ReportDateRangeValidator.Validate(query.StartDate, query.EndDate);
        if (validation.IsFailure)
        {
            return Result.Failure<SlaReportDto>(validation.Error);
        }

        var startTicks = query.StartDate.UtcTicks;
        var endTicks = query.EndDate.UtcTicks;

        var tickets = await _context.Set<Ticket>().AsNoTracking()
            .Where(t => t.CreatedAtTicks >= startTicks && t.CreatedAtTicks <= endTicks)
            .Select(t => new { t.FirstResponseCompliance, t.ResolutionCompliance })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var firstResponse = Summarize(tickets.Select(t => t.FirstResponseCompliance));
        var resolution = Summarize(tickets.Select(t => t.ResolutionCompliance));

        return Result.Success(new SlaReportDto(tickets.Count, firstResponse, resolution));
    }

    private static SlaComplianceBreakdownDto Summarize(IEnumerable<SlaComplianceStatus> values)
    {
        var cumprido = 0L;
        var violado = 0L;
        var pendente = 0L;

        foreach (var value in values)
        {
            switch (value)
            {
                case SlaComplianceStatus.Cumprido:
                    cumprido++;
                    break;
                case SlaComplianceStatus.Violado:
                    violado++;
                    break;
                default:
                    pendente++;
                    break;
            }
        }

        var denominator = cumprido + violado;
        var rate = denominator > 0 ? (decimal)cumprido / denominator : 0m;

        return new SlaComplianceBreakdownDto(cumprido, violado, pendente, rate);
    }
}
