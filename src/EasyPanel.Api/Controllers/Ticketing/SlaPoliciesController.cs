using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Ticketing;

/// <summary>
/// Política de SLA por prioridade do tenant do contexto (Fase 6 — R4.1/R4.2).
/// Consulta exige <c>chamado.view</c>; configurar exige <c>sla.manage</c>.
/// </summary>
[ApiController]
[Route("api/v1/sla-policies")]
public sealed class SlaPoliciesController(ISlaPolicyService slaPolicyService) : ControllerBase
{
    private readonly ISlaPolicyService _slaPolicyService = slaPolicyService;

    /// <summary>Lista as políticas de SLA configuradas do tenant.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(typeof(IReadOnlyList<SlaPolicyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _slaPolicyService.ListAsync(cancellationToken).ConfigureAwait(false);
        return Ok(result.Value.Select(ToResponse).ToList());
    }

    /// <summary>Cria ou atualiza (upsert por prioridade) uma política de SLA (R4.1).</summary>
    [HttpPost]
    [RequirePermission(Permissions.SlaManage)]
    [ProducesResponseType(typeof(SlaPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Set(SetSlaPolicyApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new SetSlaPolicyRequest(request.Priority, request.FirstResponseMinutes, request.ResolutionMinutes);
        var result = await _slaPolicyService.SetAsync(domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static SlaPolicyResponse ToResponse(SlaPolicyDto p) => new(p.Id, p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes);

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
