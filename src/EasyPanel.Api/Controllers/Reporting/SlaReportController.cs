using System.Globalization;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Reporting;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>
/// Relatório de cumprimento de SLA por período de abertura de chamado, do
/// tenant do contexto (Fase 9 — R4/R5). Exige <c>relatorio.view</c>.
/// </summary>
[ApiController]
[Route("api/v1/reports/sla")]
public sealed class SlaReportController(ISlaReportService reportService) : ControllerBase
{
    private readonly ISlaReportService _reportService = reportService;

    /// <summary>Agrega os Chamados abertos no intervalo por cumprimento de SLA de primeira resposta e de resolução (R4.1/R4.2).</summary>
    [HttpGet]
    [RequirePermission(Permissions.RelatorioView)]
    [ProducesResponseType(typeof(SlaReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] DateTimeOffset startDate, [FromQuery] DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetAsync(new SlaReportQuery(startDate, endDate), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Exporta o mesmo relatório em CSV (R5) — uma linha por indicador (primeira resposta / resolução).</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.RelatorioView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] DateTimeOffset startDate, [FromQuery] DateTimeOffset endDate, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetAsync(new SlaReportQuery(startDate, endDate), cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var dto = result.Value;
        var headers = new[] { "Indicator", "Cumprido", "Violado", "Pendente", "ComplianceRate" };
        var rows = new List<IReadOnlyList<string>>
        {
            ToRow("FirstResponse", dto.FirstResponse),
            ToRow("Resolution", dto.Resolution),
        };

        var csv = ReportCsvWriter.Write(headers, rows);
        return File(csv, "text/csv; charset=utf-8", "sla.csv");
    }

    private static IReadOnlyList<string> ToRow(string indicator, SlaComplianceBreakdownDto breakdown) =>
    [
        indicator,
        breakdown.Cumprido.ToString(CultureInfo.InvariantCulture),
        breakdown.Violado.ToString(CultureInfo.InvariantCulture),
        breakdown.Pendente.ToString(CultureInfo.InvariantCulture),
        breakdown.ComplianceRate.ToString(CultureInfo.InvariantCulture),
    ];

    private static SlaReportResponse ToResponse(SlaReportDto dto) => new(
        dto.TotalTickets,
        new SlaComplianceBreakdownResponse(dto.FirstResponse.Cumprido, dto.FirstResponse.Violado, dto.FirstResponse.Pendente, dto.FirstResponse.ComplianceRate),
        new SlaComplianceBreakdownResponse(dto.Resolution.Cumprido, dto.Resolution.Violado, dto.Resolution.Pendente, dto.Resolution.ComplianceRate));

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
