using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Ticketing;

/// <summary>
/// Anexos de um Chamado do tenant do contexto, sobre o MinIO (Fase 6 — R5).
/// Envio exige <c>chamado.manage</c>; consulta/listagem/download exigem
/// <c>chamado.view</c>. O tenant vem sempre do token; cross-tenant → 404.
/// </summary>
[ApiController]
[Route("api/v1/tickets/{ticketId:guid}/attachments")]
public sealed class TicketAttachmentsController(ITicketAttachmentService attachmentService) : ControllerBase
{
    private readonly ITicketAttachmentService _attachmentService = attachmentService;

    /// <summary>Envia um anexo para o chamado, validando tamanho/tipo antes de gravar no storage (R5.1/R5.2).</summary>
    [HttpPost]
    [RequirePermission(Permissions.ChamadoManage)]
    [ProducesResponseType(typeof(TicketAttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upload(Guid ticketId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ToProblem(TicketingErrors.AttachmentTooLarge, StatusCodes.Status400BadRequest, "Requisição inválida"));
        }

        await using var stream = file.OpenReadStream();
        var request = new UploadTicketAttachmentRequest(file.FileName, file.ContentType, file.Length);

        var result = await _attachmentService.UploadAsync(ticketId, request, stream, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(List), new { ticketId }, response);
    }

    /// <summary>Lista os anexos de um chamado, restrito ao tenant (R5.4).</summary>
    [HttpGet]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(typeof(IReadOnlyList<TicketAttachmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid ticketId, CancellationToken cancellationToken)
    {
        var result = await _attachmentService.ListAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        return Ok(result.Value.Select(ToResponse).ToList());
    }

    /// <summary>Baixa o conteúdo de um anexo, restrito ao chamado/tenant (R5.3).</summary>
    [HttpGet("{attachmentId:guid}")]
    [RequirePermission(Permissions.ChamadoView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid ticketId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var result = await _attachmentService.DownloadAsync(ticketId, attachmentId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var download = result.Value;
        return File(download.Content, download.ContentType, download.FileName);
    }

    private static TicketAttachmentResponse ToResponse(TicketAttachmentDto a) => new(
        a.Id, a.TicketId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.UploadedAt);

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
