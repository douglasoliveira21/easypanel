using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Billing;

/// <summary>
/// Consulta e ciclo de vida de Faturas do tenant do contexto (Fase 8 —
/// R5/R6). Consultas exigem <c>fechamento.view</c>; transicionar status
/// exige <c>fechamento.manage</c>. O tenant vem sempre do token;
/// cross-tenant → 404.
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
public sealed class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    private readonly IInvoiceService _invoiceService = invoiceService;

    /// <summary>Lista faturas do tenant, com filtros e paginação (R6.1).</summary>
    [HttpGet]
    [RequirePermission(Permissions.FechamentoView)]
    [ProducesResponseType(typeof(InvoicePageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? contractId = null,
        [FromQuery] InvoiceStatus? status = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken cancellationToken = default)
    {
        var query = new InvoiceQuery(new PageRequest(page, pageSize), customerId, contractId, status, year, month);
        var result = await _invoiceService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new InvoicePageResponse(
            paged.Items.Select(ToResponse).ToList(), paged.Page, paged.PageSize, paged.TotalCount));
    }

    /// <summary>Consulta uma fatura por id, com seus Itens, restrita ao tenant (R6.2).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.FechamentoView)]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _invoiceService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Transiciona o status de uma fatura conforme a máquina de estados (R5.1–R5.4).</summary>
    [HttpPost("{id:guid}/status")]
    [RequirePermission(Permissions.FechamentoManage)]
    [ProducesResponseType(typeof(InvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeInvoiceStatusApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new ChangeInvoiceStatusRequest(request.ToStatus);
        var result = await _invoiceService.ChangeStatusAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static InvoiceResponse ToResponse(InvoiceDto i) => new(
        i.Id, i.ContractId, i.CustomerId, i.PeriodStart, i.PeriodEnd, i.Status, i.TotalAmount, i.Currency,
        i.GeneratedAt, i.IssuedAt, i.CancelledAt, i.Items.Select(ToResponse).ToList());

    private static InvoiceLineItemResponse ToResponse(InvoiceLineItemDto li) => new(
        li.Id, li.InvoiceId, li.PrinterId, li.CounterType, li.CounterTypeLabel, li.ConsumedQuantity,
        li.IncludedQuantity, li.ExcessQuantity, li.UnitPrice, li.LineAmount);

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
