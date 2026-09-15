using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>
/// CRUD de regras de alerta do tenant do contexto (R1). Autorização granular:
/// leitura → <c>alert.view</c>; criação/edição → <c>alert.manage</c>. O tenant vem
/// sempre do token; cross-tenant → 404. Falhas de <see cref="Result"/> são
/// mapeadas para HTTP por <see cref="ErrorType"/>. <see cref="AlertRule.WebhookSecret"/>
/// nunca é retornado (R5.6).
/// </summary>
[ApiController]
[Route("api/v1/alert-rules")]
public sealed class AlertRulesController(IAlertRuleService alertRuleService) : ControllerBase
{
    private readonly IAlertRuleService _alertRuleService = alertRuleService;

    /// <summary>Lista regras de alerta do tenant, com filtros e paginação (R1.6).</summary>
    [HttpGet]
    [RequirePermission(Permissions.AlertView)]
    [ProducesResponseType(typeof(AlertRulePageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AlertRulePageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] bool? isActive = null,
        [FromQuery] AlertRuleScopeType? scopeType = null,
        [FromQuery] int? eventType = null,
        CancellationToken cancellationToken = default)
    {
        var query = new AlertRuleQuery(new PageRequest(page, pageSize), isActive, scopeType, eventType);
        var result = await _alertRuleService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new AlertRulePageResponse(
            paged.Items.Select(ToResponse).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount));
    }

    /// <summary>Consulta uma regra de alerta por id, restrita ao tenant (R1.6).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.AlertView)]
    [ProducesResponseType(typeof(AlertRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _alertRuleService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Cria uma regra de alerta vinculada ao tenant do contexto (R1.2).</summary>
    [HttpPost]
    [RequirePermission(Permissions.AlertManage)]
    [ProducesResponseType(typeof(AlertRuleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateAlertRuleApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new CreateAlertRuleRequest(
            request.Name,
            request.Description,
            request.EventTypes,
            request.ScopeType,
            request.ScopeLocationId,
            request.ScopePrinterId,
            request.ScopeWindowsClientId,
            request.Severity,
            request.ThresholdCount,
            request.ThresholdWindowMinutes,
            request.AutoResolve,
            request.EmailEnabled,
            request.EmailRecipients,
            request.WebhookEnabled,
            request.WebhookUrl,
            request.WebhookSecret);

        var result = await _alertRuleService.CreateAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>Atualiza uma regra de alerta do próprio tenant (R1).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.AlertManage)]
    [ProducesResponseType(typeof(AlertRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateAlertRuleApiRequest request,
        CancellationToken cancellationToken)
    {
        var domain = new UpdateAlertRuleRequest(
            request.Name,
            request.Description,
            request.IsActive,
            request.EventTypes,
            request.ScopeType,
            request.ScopeLocationId,
            request.ScopePrinterId,
            request.ScopeWindowsClientId,
            request.Severity,
            request.ThresholdCount,
            request.ThresholdWindowMinutes,
            request.AutoResolve,
            request.EmailEnabled,
            request.EmailRecipients,
            request.WebhookEnabled,
            request.WebhookUrl,
            request.WebhookSecret);

        var result = await _alertRuleService.UpdateAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static AlertRuleResponse ToResponse(AlertRuleDto d) => new(
        d.Id,
        d.TenantId,
        d.Name,
        d.Description,
        d.IsActive,
        d.EventTypes,
        d.ScopeType,
        d.ScopeLocationId,
        d.ScopePrinterId,
        d.ScopeWindowsClientId,
        d.Severity,
        d.ThresholdCount,
        d.ThresholdWindowMinutes,
        d.AutoResolve,
        d.EmailEnabled,
        d.EmailRecipients,
        d.WebhookEnabled,
        d.WebhookUrl,
        d.CreatedAt,
        d.UpdatedAt);

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
