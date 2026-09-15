using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Contracts;

/// <summary>
/// Cadastro, ciclo de vida e resolução de Contratos do tenant do contexto
/// (Fase 7 — R1/R4/R5). Consultas exigem <c>contrato.view</c>; criar/atualizar
/// exige <c>contrato.manage</c>. O tenant vem sempre do token; cross-tenant →
/// 404.
/// </summary>
[ApiController]
public sealed class ContractsController(IContractService contractService) : ControllerBase
{
    private readonly IContractService _contractService = contractService;

    /// <summary>Lista contratos do tenant, com filtros e paginação (R1.4).</summary>
    [HttpGet("api/v1/contracts")]
    [RequirePermission(Permissions.ContratoView)]
    [ProducesResponseType(typeof(ContractPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] Guid? customerId = null,
        [FromQuery] ContractStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = new ContractQuery(new PageRequest(page, pageSize), customerId, status);
        var result = await _contractService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new ContractPageResponse(
            paged.Items.Select(ToResponse).ToList(), paged.Page, paged.PageSize, paged.TotalCount));
    }

    /// <summary>Consulta um contrato por id, restrito ao tenant (R1.4).</summary>
    [HttpGet("api/v1/contracts/{id:guid}")]
    [RequirePermission(Permissions.ContratoView)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _contractService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Cria um contrato vinculado ao tenant do contexto (R1.2/R1.3).</summary>
    [HttpPost("api/v1/contracts")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateContractApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new CreateContractRequest(request.Number, request.CustomerId, request.StartDate, request.EndDate, request.Observations);
        var result = await _contractService.CreateAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>Atualiza campos editáveis de um contrato do próprio tenant (R1).</summary>
    [HttpPut("api/v1/contracts/{id:guid}")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateContractApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new UpdateContractRequest(request.Number, request.EndDate, request.Observations);
        var result = await _contractService.UpdateAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Transiciona o status de um contrato conforme a máquina de estados (R5.1/R5.2).</summary>
    [HttpPost("api/v1/contracts/{id:guid}/status")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeStatus(Guid id, ChangeContractStatusApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new ChangeContractStatusRequest(request.ToStatus);
        var result = await _contractService.ChangeStatusAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>
    /// Resolve o contrato aplicável a uma Impressora numa data (R4), na ordem
    /// Impressora → Local → Cliente inteiro. <c>204</c> quando nenhum contrato
    /// se aplica (R4.2, não é erro).
    /// </summary>
    [HttpGet("api/v1/printers/{printerId:guid}/applicable-contract")]
    [RequirePermission(Permissions.ContratoView)]
    [ProducesResponseType(typeof(ContractResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResolveApplicable(
        Guid printerId, [FromQuery] DateTimeOffset? referenceDate, CancellationToken cancellationToken)
    {
        var date = referenceDate ?? DateTimeOffset.UtcNow;
        var result = await _contractService.ResolveApplicableAsync(printerId, date, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        return result.Value is null ? NoContent() : Ok(ToResponse(result.Value));
    }

    private static ContractResponse ToResponse(ContractDto c) => new(
        c.Id, c.TenantId, c.Number, c.CustomerId, c.StartDate, c.EndDate, c.Status, c.Observations, c.IsExpired, c.CreatedAt, c.UpdatedAt);

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
