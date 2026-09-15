using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Supplies;

/// <summary>
/// Consulta de níveis atuais, previsão de troca e histórico de suprimento de uma
/// impressora (Fase 4 — R3/R5). Requer <c>supply.view</c>. Tudo restrito ao tenant
/// do contexto (cross-tenant → 404). Histórico por cursor pagination (grandes
/// volumes), mesmo padrão de <c>CountersController</c> da Fase 2.
/// </summary>
[ApiController]
[Route("api/v1/printers/{printerId:guid}/supplies")]
public sealed class SuppliesController(ISupplyService supplyService) : ControllerBase
{
    private readonly ISupplyService _supplyService = supplyService;

    /// <summary>Níveis atuais de cada rótulo de suprimento, com previsão de troca (R3.4/R5).</summary>
    [HttpGet]
    [RequirePermission(Permissions.SupplyView)]
    [ProducesResponseType(typeof(IReadOnlyList<SupplyLevelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentLevels(Guid printerId, CancellationToken cancellationToken)
    {
        var result = await _supplyService.GetCurrentLevelsAsync(printerId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        return Ok(result.Value.Select(ToResponse).ToList());
    }

    /// <summary>Histórico de leituras por cursor pagination, com filtro opcional por rótulo (R3.3).</summary>
    [HttpGet("history")]
    [RequirePermission(Permissions.SupplyView)]
    [ProducesResponseType(typeof(SupplyCursorPageResponse<SupplyReadingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListHistory(
        Guid printerId,
        [FromQuery] string? label = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _supplyService
            .ListHistoryAsync(printerId, label, cursor, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new SupplyCursorPageResponse<SupplyReadingResponse>(
            page.Items.Select(ToResponse).ToList(),
            page.NextCursor));
    }

    private static SupplyLevelResponse ToResponse(SupplyLevelDto d) =>
        new(d.PrinterId, d.Label, d.Percent, d.Timestamp, d.ForecastDepletionAt);

    private static SupplyReadingResponse ToResponse(SupplyReadingDto d) =>
        new(d.Id, d.PrinterId, d.Label, d.Percent, d.Timestamp);

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
