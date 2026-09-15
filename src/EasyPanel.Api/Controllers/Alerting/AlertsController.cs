using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>
/// Consulta e ciclo de vida de Alertas do tenant do contexto (R3/R6). Autorização
/// granular: leitura → <c>alert.view</c>; reconhecer/resolver → <c>alert.acknowledge</c>.
/// Listagem e histórico de notificação por cursor pagination (grandes volumes —
/// R3.6/R6.2). Tudo restrito ao tenant do contexto (cross-tenant → 404).
/// </summary>
[ApiController]
[Route("api/v1/alerts")]
public sealed class AlertsController(IAlertService alertService) : ControllerBase
{
    private const int DefaultPageSize = 50;

    private readonly IAlertService _alertService = alertService;

    /// <summary>Lista Alertas do tenant por cursor pagination, com filtros (R3.6).</summary>
    [HttpGet]
    [RequirePermission(Permissions.AlertView)]
    [ProducesResponseType(typeof(AlertCursorPageResponse<AlertResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] AlertState? state = null,
        [FromQuery] AlertSeverity? severity = null,
        [FromQuery] Guid? alertRuleId = null,
        [FromQuery] Guid? printerId = null,
        [FromQuery] Guid? windowsClientId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var query = new AlertQuery(state, severity, alertRuleId, printerId, windowsClientId, from, to, cursor, pageSize);
        var result = await _alertService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var page = result.Value;

        return Ok(new AlertCursorPageResponse<AlertResponse>(
            page.Items.Select(a => ToResponse(a)).ToList(),
            page.NextCursor));
    }

    /// <summary>Consulta um Alerta por id, incluindo seu histórico de transições (R3.7).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.AlertView)]
    [ProducesResponseType(typeof(AlertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _alertService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Reconhece um Alerta em aberto do próprio tenant (R3.3).</summary>
    [HttpPost("{id:guid}/acknowledge")]
    [RequirePermission(Permissions.AlertAcknowledge)]
    [ProducesResponseType(typeof(AlertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        var result = await _alertService.AcknowledgeAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Resolve manualmente um Alerta do próprio tenant (R3.4).</summary>
    [HttpPost("{id:guid}/resolve")]
    [RequirePermission(Permissions.AlertAcknowledge)]
    [ProducesResponseType(typeof(AlertResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(
        Guid id,
        ResolveAlertApiRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _alertService
            .ResolveAsync(id, new ResolveAlertRequest(request.Note), cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Lista o histórico de notificações de um Alerta por cursor pagination (R6.2).</summary>
    [HttpGet("{id:guid}/notifications")]
    [RequirePermission(Permissions.AlertView)]
    [ProducesResponseType(typeof(AlertCursorPageResponse<AlertNotificationAttemptResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListNotifications(
        Guid id,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _alertService.ListNotificationsAsync(id, cursor, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new AlertCursorPageResponse<AlertNotificationAttemptResponse>(
            page.Items.Select(ToResponse).ToList(),
            page.NextCursor));
    }

    private static AlertResponse ToResponse(AlertDto d) => new(
        d.Id,
        d.TenantId,
        d.AlertRuleId,
        d.Severity,
        d.State,
        d.PrinterId,
        d.WindowsClientId,
        d.FirstOccurrenceAt,
        d.LastOccurrenceAt,
        d.OccurrenceCount,
        d.AcknowledgedAt,
        d.AcknowledgedByUserId,
        d.ResolvedAt,
        d.ResolvedByUserId,
        d.AutoResolved,
        d.ResolutionNote,
        d.Transitions?.Select(ToResponse).ToList());

    private static AlertTransitionResponse ToResponse(AlertTransitionDto d) => new(
        d.Id,
        d.FromState,
        d.ToState,
        d.ActorUserId,
        d.OccurredAt,
        d.Note);

    private static AlertNotificationAttemptResponse ToResponse(AlertNotificationAttemptDto d) => new(
        d.Id,
        d.AlertId,
        d.Channel,
        d.Outcome,
        d.AttemptNumber,
        d.HttpStatusCode,
        d.ErrorSummary,
        d.AttemptedAt);

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
