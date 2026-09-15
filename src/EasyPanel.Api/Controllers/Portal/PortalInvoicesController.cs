using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Portal;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Portal;

/// <summary>
/// Portal do Cliente (Fase 10 — R5): Faturas restritas ao Cliente vinculado ao
/// usuário autenticado (<c>ICustomerContext</c>), somente-leitura, sempre
/// excluindo Faturas em Rascunho (R5.2). Exige <c>portal.fatura.view</c>
/// (exclusiva do papel <c>Cliente</c>). Fatura de outro Cliente/tenant ou em
/// Rascunho → 404 uniforme.
/// </summary>
[ApiController]
public sealed class PortalInvoicesController(IPortalInvoiceService invoiceService) : ControllerBase
{
    private const int DefaultPageSize = 50;

    private readonly IPortalInvoiceService _invoiceService = invoiceService;

    /// <summary>Lista as Faturas emitidas/canceladas do Cliente do usuário autenticado, por cursor (R5.1).</summary>
    [HttpGet("api/v1/portal/invoices")]
    [RequirePermission(Permissions.PortalFaturaView)]
    [ProducesResponseType(typeof(PortalCursorPageResponse<PortalInvoiceSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _invoiceService.ListAsync(cursor, pageSize, cancellationToken).ConfigureAwait(false);

        var page = result.Value;
        return Ok(new PortalCursorPageResponse<PortalInvoiceSummaryResponse>(
            page.Items.Select(ToSummaryResponse).ToList(), page.NextCursor));
    }

    /// <summary>
    /// Detalha uma Fatura do Cliente do usuário autenticado, com itens de
    /// excedente (R5.3). Fatura de outro Cliente/tenant ou em Rascunho → 404.
    /// </summary>
    [HttpGet("api/v1/portal/invoices/{id:guid}")]
    [RequirePermission(Permissions.PortalFaturaView)]
    [ProducesResponseType(typeof(PortalInvoiceDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _invoiceService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToDetailResponse(result.Value)) : MapFailure(result.Error);
    }

    private static PortalInvoiceSummaryResponse ToSummaryResponse(PortalInvoiceSummaryDto i) => new(
        i.Id, i.PeriodStart, i.PeriodEnd, i.Status, i.TotalAmount);

    private static PortalInvoiceDetailResponse ToDetailResponse(PortalInvoiceDetailDto i) => new(
        i.Id,
        i.PeriodStart,
        i.PeriodEnd,
        i.Status,
        i.TotalAmount,
        i.LineItems.Select(li => new PortalInvoiceLineItemResponse(
            li.PrinterId, li.CounterType, li.ConsumedQuantity, li.IncludedQuantity, li.ExcessQuantity,
            li.UnitPrice, li.LineAmount)).ToList());

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
