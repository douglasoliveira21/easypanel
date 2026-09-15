using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Billing;

/// <summary>
/// Execução e histórico de fechamento mensal do tenant do contexto (Fase 8
/// — R1/R2/R3/R4). Executar exige <c>fechamento.manage</c>; consultar o
/// histórico exige <c>fechamento.view</c>.
/// </summary>
[ApiController]
[Route("api/v1/billing-closings")]
public sealed class BillingClosingsController(IBillingClosingService billingClosingService) : ControllerBase
{
    private readonly IBillingClosingService _billingClosingService = billingClosingService;

    /// <summary>
    /// Executa o fechamento do período informado: consolida consumo por
    /// Impressora, resolve o Contrato aplicável, calcula excedente contra a
    /// Franquia e gera Faturas (R1–R4). Período corrente/futuro → 400;
    /// período já fechado → 409.
    /// </summary>
    [HttpPost]
    [RequirePermission(Permissions.FechamentoManage)]
    [ProducesResponseType(typeof(BillingClosingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Execute(ExecuteBillingClosingApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new ExecuteBillingClosingRequest(request.Year, request.Month);
        var result = await _billingClosingService.ExecuteAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(result.Value));
    }

    /// <summary>Lista o histórico de fechamentos executados, restrito ao tenant (R6.3).</summary>
    [HttpGet]
    [RequirePermission(Permissions.FechamentoView)]
    [ProducesResponseType(typeof(BillingClosingPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _billingClosingService
            .ListAsync(new PageRequest(page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        var paged = result.Value;
        return Ok(new BillingClosingPageResponse(
            paged.Items.Select(ToResponse).ToList(), paged.Page, paged.PageSize, paged.TotalCount));
    }

    private static BillingClosingResponse ToResponse(BillingClosingDto c) => new(
        c.Id, c.Year, c.Month, c.PeriodStart, c.PeriodEnd, c.ExecutedAt, c.ExecutedByUserId, c.InvoiceCount);

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
