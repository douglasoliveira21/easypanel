using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>
/// CRUD (criação/listagem/encerramento) de silenciamentos de alerta do tenant do
/// contexto (R7). Autorização granular: leitura → <c>alert.view</c>; gestão →
/// <c>alert.manage</c>. Escopo obrigatório (regra e/ou impressora/agente) e período
/// válido são validados no serviço → 400; encerrar um silenciamento já encerrado →
/// 409. Tudo restrito ao tenant do contexto (cross-tenant → 404).
/// </summary>
[ApiController]
[Route("api/v1/alert-silences")]
public sealed class AlertSilencesController(IAlertSilenceService alertSilenceService) : ControllerBase
{
    private readonly IAlertSilenceService _alertSilenceService = alertSilenceService;

    /// <summary>Lista silenciamentos do tenant, com paginação (R7.6).</summary>
    [HttpGet]
    [RequirePermission(Permissions.AlertView)]
    [ProducesResponseType(typeof(AlertSilencePageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AlertSilencePageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        var query = new AlertSilenceQuery(new PageRequest(page, pageSize), activeOnly);
        var result = await _alertSilenceService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new AlertSilencePageResponse(
            paged.Items.Select(ToResponse).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount));
    }

    /// <summary>Cria um silenciamento vinculado ao tenant do contexto (R7.2).</summary>
    [HttpPost]
    [RequirePermission(Permissions.AlertManage)]
    [ProducesResponseType(typeof(AlertSilenceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateAlertSilenceApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new CreateAlertSilenceRequest(
            request.AlertRuleId,
            request.PrinterId,
            request.WindowsClientId,
            request.StartsAt,
            request.EndsAt,
            request.Reason);

        var result = await _alertSilenceService.CreateAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Encerra antecipadamente um silenciamento do próprio tenant (R7.5).</summary>
    [HttpPost("{id:guid}/end")]
    [RequirePermission(Permissions.AlertManage)]
    [ProducesResponseType(typeof(AlertSilenceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EndEarly(Guid id, CancellationToken cancellationToken)
    {
        var result = await _alertSilenceService.EndEarlyAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static AlertSilenceResponse ToResponse(AlertSilenceDto d) => new(
        d.Id,
        d.TenantId,
        d.AlertRuleId,
        d.PrinterId,
        d.WindowsClientId,
        d.StartsAt,
        d.EndsAt,
        d.CreatedByUserId,
        d.Reason,
        d.EndedEarlyAt,
        d.EndedEarlyByUserId,
        d.CreatedAt);

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
