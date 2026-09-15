using System.Globalization;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Reporting;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>
/// Relatório de faturamento por período do tenant do contexto (Fase 9 —
/// R3/R5). Exige <c>relatorio.view</c>.
/// </summary>
[ApiController]
[Route("api/v1/reports/billing")]
public sealed class BillingReportController(IBillingReportService reportService) : ControllerBase
{
    private readonly IBillingReportService _reportService = reportService;

    /// <summary>Consulta o total faturado por Cliente no intervalo, com filtro opcional por status (R3.1/R3.2).</summary>
    [HttpGet]
    [RequirePermission(Permissions.RelatorioView)]
    [ProducesResponseType(typeof(BillingReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] DateTimeOffset startDate,
        [FromQuery] DateTimeOffset endDate,
        [FromQuery] ReportingInvoiceStatus? status,
        CancellationToken cancellationToken)
    {
        var result = await _reportService
            .GetAsync(new BillingReportQuery(startDate, endDate, status), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Exporta o mesmo relatório em CSV (R5).</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.RelatorioView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] DateTimeOffset startDate,
        [FromQuery] DateTimeOffset endDate,
        [FromQuery] ReportingInvoiceStatus? status,
        CancellationToken cancellationToken)
    {
        var result = await _reportService
            .GetAsync(new BillingReportQuery(startDate, endDate, status), cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var headers = new[] { "CustomerId", "InvoiceCount", "TotalAmount" };
        var rows = result.Value.Rows.Select(r => (IReadOnlyList<string>)
        [
            r.CustomerId.ToString(),
            r.InvoiceCount.ToString(CultureInfo.InvariantCulture),
            r.TotalAmount.ToString(CultureInfo.InvariantCulture),
        ]);

        var csv = ReportCsvWriter.Write(headers, rows);
        return File(csv, "text/csv; charset=utf-8", "billing.csv");
    }

    private static BillingReportResponse ToResponse(BillingReportDto dto) => new(
        dto.Rows.Select(r => new BillingReportRowResponse(r.CustomerId, r.InvoiceCount, r.TotalAmount)).ToList(),
        dto.TotalInvoiceCount,
        dto.GrandTotal);

    private IActionResult MapFailure(Error error) => error.Type switch
    {
        ErrorType.NotFound => NotFound(ToProblem(error, StatusCodes.Status404NotFound, "Não encontrado")),
        ErrorType.Conflict => Conflict(ToProblem(error, StatusCodes.Status409Conflict, "Conflito")),
        ErrorType.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ToProblem(error, StatusCodes.Status403Forbidden, "Proibido")),
        _ => BadRequest(ToProblem(error, StatusCodes.Status400BadRequest, "Requisição inválida")),
    };

    private static ProblemDetails ToProblem(Error error, int status, string title) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = error.Message,
            Type = error.Code,
        };
}
