using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Contracts;

/// <summary>
/// Franquia por tipo de contador de um Contrato do tenant do contexto (Fase
/// 7 — R3). Consulta exige <c>contrato.view</c>; configurar exige
/// <c>contrato.manage</c>.
/// </summary>
[ApiController]
[Route("api/v1/contracts/{contractId:guid}/franchises")]
public sealed class ContractFranchisesController(IContractFranchiseService franchiseService) : ControllerBase
{
    private readonly IContractFranchiseService _franchiseService = franchiseService;

    /// <summary>Lista as franquias do contrato (R3.4).</summary>
    [HttpGet]
    [RequirePermission(Permissions.ContratoView)]
    [ProducesResponseType(typeof(IReadOnlyList<ContractFranchiseResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid contractId, CancellationToken cancellationToken)
    {
        var result = await _franchiseService.ListAsync(contractId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        return Ok(result.Value.Select(ToResponse).ToList());
    }

    /// <summary>Cria ou atualiza (upsert por tipo de contador) uma franquia (R3.1/R3.2/R3.3).</summary>
    [HttpPost]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(typeof(ContractFranchiseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Set(Guid contractId, SetContractFranchiseApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new SetContractFranchiseRequest(
            request.CounterType, request.CounterTypeLabel, request.IncludedQuantity, request.ExcessUnitPrice);
        var result = await _franchiseService.SetAsync(contractId, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static ContractFranchiseResponse ToResponse(ContractFranchiseDto f) => new(
        f.Id, f.ContractId, f.CounterType, f.CounterTypeLabel, f.IncludedQuantity, f.ExcessUnitPrice, f.Currency);

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
