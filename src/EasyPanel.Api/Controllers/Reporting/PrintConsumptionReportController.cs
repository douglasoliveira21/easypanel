using System.Globalization;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Reporting;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>
/// Relatório de consumo de impressão por período do tenant do contexto
/// (Fase 9 — R2/R5). Exige <c>relatorio.view</c>.
/// </summary>
[ApiController]
[Route("api/v1/reports/print-consumption")]
public sealed class PrintConsumptionReportController(IPrintConsumptionReportService reportService) : ControllerBase
{
    private readonly IPrintConsumptionReportService _reportService = reportService;

    /// <summary>Consulta o consumo por (Impressora, CounterType) no intervalo, agrupado por Cliente/Local (R2.1/R2.2).</summary>
    [HttpGet]
    [RequirePermission(Permissions.RelatorioView)]
    [ProducesResponseType(typeof(PrintConsumptionReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] DateTimeOffset startDate,
        [FromQuery] DateTimeOffset endDate,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? locationId,
        CancellationToken cancellationToken)
    {
        var result = await _reportService
            .GetAsync(new PrintConsumptionReportQuery(startDate, endDate, customerId, locationId), cancellationToken)
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
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? locationId,
        CancellationToken cancellationToken)
    {
        var result = await _reportService
            .GetAsync(new PrintConsumptionReportQuery(startDate, endDate, customerId, locationId), cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var headers = new[] { "CustomerId", "LocationId", "PrinterId", "CounterType", "Consumed" };
        var rows = result.Value.Rows.Select(r => (IReadOnlyList<string>)
        [
            r.CustomerId.ToString(),
            r.LocationId.ToString(),
            r.PrinterId.ToString(),
            r.CounterType.ToString(),
            r.Consumed.ToString(CultureInfo.InvariantCulture),
        ]);

        var csv = ReportCsvWriter.Write(headers, rows);
        return File(csv, "text/csv; charset=utf-8", "print-consumption.csv");
    }

    private static PrintConsumptionReportResponse ToResponse(PrintConsumptionReportDto dto) => new(
        dto.Rows.Select(r => new PrintConsumptionRowResponse(r.CustomerId, r.LocationId, r.PrinterId, r.CounterType, r.Consumed)).ToList(),
        dto.StartDate,
        dto.EndDate);

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
