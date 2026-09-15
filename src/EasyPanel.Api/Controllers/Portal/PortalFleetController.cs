using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Portal;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Portal;

/// <summary>
/// Portal do Cliente (Fase 10 — R3): parque de Impressoras e histórico de
/// contadores restritos ao Cliente vinculado ao usuário autenticado
/// (<c>ICustomerContext</c>). Exige <c>portal.parque.view</c> (exclusiva do
/// papel <c>Cliente</c>). Recurso de outro Cliente/tenant → 404 uniforme.
/// </summary>
[ApiController]
public sealed class PortalFleetController(IPortalFleetService fleetService) : ControllerBase
{
    private const int DefaultPageSize = 50;

    private readonly IPortalFleetService _fleetService = fleetService;

    /// <summary>Lista as Impressoras do Cliente do usuário autenticado, por cursor (R3.1).</summary>
    [HttpGet("api/v1/portal/printers")]
    [RequirePermission(Permissions.PortalParqueView)]
    [ProducesResponseType(typeof(PortalCursorPageResponse<PortalPrinterResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListPrinters(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _fleetService.ListPrintersAsync(cursor, pageSize, cancellationToken).ConfigureAwait(false);

        var page = result.Value;
        return Ok(new PortalCursorPageResponse<PortalPrinterResponse>(
            page.Items.Select(ToResponse).ToList(), page.NextCursor));
    }

    /// <summary>
    /// Histórico de contadores de uma Impressora do Cliente do usuário
    /// autenticado, por cursor (R3.1). Impressora de outro Cliente/tenant → 404 (R3.2).
    /// </summary>
    [HttpGet("api/v1/portal/printers/{id:guid}/counters")]
    [RequirePermission(Permissions.PortalParqueView)]
    [ProducesResponseType(typeof(PortalCursorPageResponse<PortalCounterReadingResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCounterHistory(
        Guid id,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _fleetService.GetCounterHistoryAsync(id, cursor, pageSize, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new PortalCursorPageResponse<PortalCounterReadingResponse>(
            page.Items.Select(ToResponse).ToList(), page.NextCursor));
    }

    private static PortalPrinterResponse ToResponse(PortalPrinterDto p) => new(
        p.Id, p.Fabricante, p.Modelo, p.NumeroSerie, p.LocationId, p.LocationName, p.Status);

    private static PortalCounterReadingResponse ToResponse(PortalCounterReadingDto c) => new(
        c.Timestamp, c.CounterType, c.Value);

    private IActionResult MapFailure(Error error) => error.Type switch
    {
        ErrorType.NotFound => NotFound(ToProblem(error, StatusCodes.Status404NotFound, "Não encontrado")),
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
