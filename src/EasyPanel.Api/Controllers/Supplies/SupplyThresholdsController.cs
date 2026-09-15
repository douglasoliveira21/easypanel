using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Supplies;

/// <summary>
/// Gestão de limiares de suprimento do tenant do contexto (Fase 4 — R4). Leitura
/// exige <c>supply.view</c>; criação/atualização (upsert por Impressora+Rótulo) e
/// remoção exigem <c>supply.manage</c>. Tudo restrito ao tenant do contexto
/// (cross-tenant → 404); percentual fora de 0–100 → 400.
/// </summary>
[ApiController]
[Route("api/v1/supply-thresholds")]
public sealed class SupplyThresholdsController(ISupplyService supplyService) : ControllerBase
{
    private readonly ISupplyService _supplyService = supplyService;

    /// <summary>Lista os limiares configurados do tenant, com paginação (R4).</summary>
    [HttpGet]
    [RequirePermission(Permissions.SupplyView)]
    [ProducesResponseType(typeof(SupplyThresholdPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SupplyThresholdPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _supplyService
            .ListThresholdsAsync(new SupplyThresholdQuery(new PageRequest(page, pageSize)), cancellationToken)
            .ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new SupplyThresholdPageResponse(
            paged.Items.Select(ToResponse).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount));
    }

    /// <summary>Cria ou atualiza (upsert por Impressora+Rótulo) um limiar de suprimento (R4.2).</summary>
    [HttpPost]
    [RequirePermission(Permissions.SupplyManage)]
    [ProducesResponseType(typeof(SupplyThresholdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Set(SetSupplyThresholdApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new SetSupplyThresholdRequest(request.PrinterId, request.Label, request.ThresholdPercent);
        var result = await _supplyService.SetThresholdAsync(domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Remove um limiar específico, voltando à cascata de resolução (R4).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.SupplyManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _supplyService.DeleteThresholdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    private static SupplyThresholdResponse ToResponse(SupplyThresholdDto d) =>
        new(d.Id, d.PrinterId, d.Label, d.ThresholdPercent);

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
