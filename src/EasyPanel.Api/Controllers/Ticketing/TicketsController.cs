using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Ticketing;

/// <summary>
/// Abertura, ciclo de vida, atribuição, interações e consulta de SLA de
/// Chamados do tenant do contexto (Fase 6 — R1/R2/R3/R4). Consultas exigem
/// <c>chamado.view</c>; abrir/atualizar exige <c>chamado.manage</c>. O tenant
/// vem sempre do token; cross-tenant → 404.
/// </summary>
[ApiController]
public sealed class TicketsController(ITicketService ticketService) : ControllerBase
{
    private const int DefaultInteractionPageSize = 50;

    private readonly ITicketService _ticketService = ticketService;

    /// <summary>Lista chamados do tenant, com filtros e paginação (R3.1/R3.2).</summary>
    [HttpGet("api/v1/tickets")]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(typeof(TicketPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] TicketStatus? status = null,
        [FromQuery] TicketPriority? priority = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? locationId = null,
        [FromQuery] Guid? printerId = null,
        [FromQuery] Guid? assignedToUserId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new TicketQuery(
            new PageRequest(page, pageSize), status, priority, customerId, locationId, printerId, assignedToUserId);

        var result = await _ticketService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new TicketPageResponse(
            paged.Items.Select(ToResponse).ToList(), paged.Page, paged.PageSize, paged.TotalCount));
    }

    /// <summary>Consulta um chamado por id, restrito ao tenant (R3.3).</summary>
    [HttpGet("api/v1/tickets/{id:guid}")]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _ticketService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Abre um chamado vinculado ao tenant do contexto, calculando os prazos de SLA (R1.2/R1.5).</summary>
    [HttpPost("api/v1/tickets")]
    [RequirePermission(Permissions.ChamadoManage)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateTicketApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new CreateTicketRequest(
            request.Title, request.Description, request.CustomerId, request.LocationId, request.PrinterId, request.Priority);

        var result = await _ticketService.CreateAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>Transiciona o status de um chamado conforme a máquina de estados (R2.2/R2.3).</summary>
    [HttpPost("api/v1/tickets/{id:guid}/status")]
    [RequirePermission(Permissions.ChamadoManage)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeTicketStatusApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new ChangeTicketStatusRequest(request.ToStatus);
        var result = await _ticketService.ChangeStatusAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Atribui, reatribui ou desatribui (<c>assignedToUserId: null</c>) um chamado (R2.4).</summary>
    [HttpPost("api/v1/tickets/{id:guid}/assign")]
    [RequirePermission(Permissions.ChamadoManage)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Assign(Guid id, AssignTicketApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new AssignTicketRequest(request.AssignedToUserId);
        var result = await _ticketService.AssignAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Registra um comentário e, quando é a 1ª resposta de um Técnico, marca o SLA de primeira resposta (R2.5/R4.4).</summary>
    [HttpPost("api/v1/tickets/{id:guid}/comments")]
    [RequirePermission(Permissions.ChamadoManage)]
    [ProducesResponseType(typeof(TicketInteractionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid id, AddTicketCommentApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new AddTicketCommentRequest(request.Comment);
        var result = await _ticketService.AddCommentAsync(id, domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(result.Value));
    }

    /// <summary>Histórico de interações de um chamado, por cursor pagination (R2.6).</summary>
    [HttpGet("api/v1/tickets/{id:guid}/interactions")]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(typeof(TicketCursorPageResponse<TicketInteractionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListInteractions(
        Guid id,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultInteractionPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _ticketService.ListInteractionsAsync(id, cursor, pageSize, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new TicketCursorPageResponse<TicketInteractionResponse>(
            page.Items.Select(ToResponse).ToList(), page.NextCursor));
    }

    /// <summary>Lista os chamados do tenant com SLA violado ou já vencido (R4.6).</summary>
    [HttpGet("api/v1/tickets/sla-breached")]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(typeof(TicketPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSlaBreached(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _ticketService
            .ListSlaBreachedAsync(new PageRequest(page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        var paged = result.Value;
        return Ok(new TicketPageResponse(
            paged.Items.Select(ToResponse).ToList(), paged.Page, paged.PageSize, paged.TotalCount));
    }

    private static TicketResponse ToResponse(TicketDto t) => new(
        t.Id, t.TenantId, t.Title, t.Description, t.CustomerId, t.LocationId, t.PrinterId, t.Priority, t.Status,
        t.RequestedByUserId, t.AssignedToUserId, t.FirstResponseDueAt, t.FirstResponseAt, t.FirstResponseCompliance,
        t.ResolutionDueAt, t.ResolvedAt, t.ResolutionCompliance, t.CreatedAt);

    private static TicketInteractionResponse ToResponse(TicketInteractionDto i) => new(
        i.Id, i.TicketId, i.Type, i.ActorUserId, i.Comment, i.FromStatus, i.ToStatus, i.AssignedToUserId, i.OccurredAt);

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
