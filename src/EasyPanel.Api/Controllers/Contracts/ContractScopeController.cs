using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Contracts;

/// <summary>
/// Escopo (Local/Impressora) de um Contrato do tenant do contexto (Fase 7 —
/// R2). Consulta exige <c>contrato.view</c>; vincular/desvincular exige
/// <c>contrato.manage</c>. O tenant vem sempre do token; cross-tenant → 404.
/// </summary>
[ApiController]
[Route("api/v1/contracts/{contractId:guid}")]
public sealed class ContractScopeController(IContractScopeService scopeService) : ControllerBase
{
    private readonly IContractScopeService _scopeService = scopeService;

    /// <summary>Lista os Locais vinculados ao contrato (R2.1).</summary>
    [HttpGet("locations")]
    [RequirePermission(Permissions.ContratoView)]
    [ProducesResponseType(typeof(IReadOnlyList<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListLocations(Guid contractId, CancellationToken cancellationToken)
    {
        var result = await _scopeService.ListLocationsAsync(contractId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : MapFailure(result.Error);
    }

    /// <summary>Vincula um Local ao contrato, validando mesmo Cliente (R2.3) e ausência de sobreposição (R2.4).</summary>
    [HttpPost("locations/{locationId:guid}")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddLocation(Guid contractId, Guid locationId, CancellationToken cancellationToken)
    {
        var result = await _scopeService.AddLocationAsync(contractId, locationId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    /// <summary>Remove o vínculo de um Local ao contrato.</summary>
    [HttpDelete("locations/{locationId:guid}")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveLocation(Guid contractId, Guid locationId, CancellationToken cancellationToken)
    {
        var result = await _scopeService.RemoveLocationAsync(contractId, locationId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    /// <summary>Lista as Impressoras vinculadas ao contrato (R2.1).</summary>
    [HttpGet("printers")]
    [RequirePermission(Permissions.ContratoView)]
    [ProducesResponseType(typeof(IReadOnlyList<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPrinters(Guid contractId, CancellationToken cancellationToken)
    {
        var result = await _scopeService.ListPrintersAsync(contractId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(result.Value) : MapFailure(result.Error);
    }

    /// <summary>Vincula uma Impressora ao contrato, validando mesmo Cliente (R2.3) e ausência de sobreposição (R2.4).</summary>
    [HttpPost("printers/{printerId:guid}")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddPrinter(Guid contractId, Guid printerId, CancellationToken cancellationToken)
    {
        var result = await _scopeService.AddPrinterAsync(contractId, printerId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

    /// <summary>Remove o vínculo de uma Impressora ao contrato.</summary>
    [HttpDelete("printers/{printerId:guid}")]
    [RequirePermission(Permissions.ContratoManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePrinter(Guid contractId, Guid printerId, CancellationToken cancellationToken)
    {
        var result = await _scopeService.RemovePrinterAsync(contractId, printerId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? NoContent() : MapFailure(result.Error);
    }

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
