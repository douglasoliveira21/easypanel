using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Portal;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Portal;

/// <summary>
/// Portal do Cliente (Fase 10 — R4): Chamados restritos ao Cliente vinculado
/// ao usuário autenticado (<c>ICustomerContext</c>), somente-leitura. Exige
/// <c>portal.chamado.view</c> (exclusiva do papel <c>Cliente</c>). Chamado de
/// outro Cliente/tenant → 404 uniforme.
/// </summary>
[ApiController]
public sealed class PortalTicketsController(IPortalTicketService ticketService) : ControllerBase
{
    private const int DefaultPageSize = 50;

    private readonly IPortalTicketService _ticketService = ticketService;

    /// <summary>Lista os Chamados do Cliente do usuário autenticado, por cursor (R4.1).</summary>
    [HttpGet("api/v1/portal/tickets")]
    [RequirePermission(Permissions.PortalChamadoView)]
    [ProducesResponseType(typeof(PortalCursorPageResponse<PortalTicketSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _ticketService.ListAsync(cursor, pageSize, cancellationToken).ConfigureAwait(false);

        var page = result.Value;
        return Ok(new PortalCursorPageResponse<PortalTicketSummaryResponse>(
            page.Items.Select(ToSummaryResponse).ToList(), page.NextCursor));
    }

    /// <summary>
    /// Detalha um Chamado do Cliente do usuário autenticado, com comentários e
    /// anexos (R4.2). Chamado de outro Cliente/tenant → 404.
    /// </summary>
    [HttpGet("api/v1/portal/tickets/{id:guid}")]
    [RequirePermission(Permissions.PortalChamadoView)]
    [ProducesResponseType(typeof(PortalTicketDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _ticketService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToDetailResponse(result.Value)) : MapFailure(result.Error);
    }

    private static PortalTicketSummaryResponse ToSummaryResponse(PortalTicketSummaryDto t) => new(
        t.Id, t.Title, t.Status, t.CreatedAt);

    private static PortalTicketDetailResponse ToDetailResponse(PortalTicketDetailDto t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.Status,
        t.CreatedAt,
        t.Comments.Select(c => new PortalTicketCommentResponse(c.OccurredAt, c.Comment)).ToList(),
        t.Attachments.Select(a => new PortalTicketAttachmentResponse(a.Id, a.FileName, a.ContentType)).ToList());

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
