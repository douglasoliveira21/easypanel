using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Inventory;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Inventory;

/// <summary>
/// Saldo por Local e estoque mínimo do tenant do contexto (Fase 5 — R3.2/R5).
/// Consultas exigem <c>estoque.view</c>; configurar mínimo exige <c>estoque.manage</c>.
/// </summary>
[ApiController]
public sealed class InventoryStockController(IInventoryMovementService movementService) : ControllerBase
{
    private readonly IInventoryMovementService _movementService = movementService;

    /// <summary>Saldo corrente de cada Item já movimentado num Local (R3.2).</summary>
    [HttpGet("api/v1/locations/{locationId:guid}/inventory")]
    [RequirePermission(Permissions.EstoqueView)]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryBalanceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBalanceByLocation(Guid locationId, CancellationToken cancellationToken)
    {
        var result = await _movementService.GetBalanceByLocationAsync(locationId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = result.Value
            .Select(b => new InventoryBalanceResponse(b.ItemId, b.LocationId, b.Quantity))
            .ToList();
        return Ok(response);
    }

    /// <summary>Cria ou atualiza (upsert) um estoque mínimo por Item+Local (R5.1).</summary>
    [HttpPost("api/v1/inventory-minimums")]
    [RequirePermission(Permissions.EstoqueManage)]
    [ProducesResponseType(typeof(InventoryMinimumResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetMinimum(SetInventoryMinimumApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new SetInventoryMinimumRequest(request.ItemId, request.LocationId, request.MinimumQuantity);
        var result = await _movementService.SetMinimumAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var m = result.Value;
        return Ok(new InventoryMinimumResponse(m.Id, m.ItemId, m.LocationId, m.MinimumQuantity));
    }

    /// <summary>Lista os (Item, Local) do tenant abaixo do mínimo configurado (R5.2).</summary>
    [HttpGet("api/v1/inventory-minimums/below")]
    [RequirePermission(Permissions.EstoqueView)]
    [ProducesResponseType(typeof(BelowMinimumPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListBelowMinimum(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _movementService
            .ListBelowMinimumAsync(new PageRequest(page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var paged = result.Value;
        var items = paged.Items
            .Select(b => new BelowMinimumResponse(b.ItemId, b.LocationId, b.CurrentQuantity, b.MinimumQuantity))
            .ToList();
        return Ok(new BelowMinimumPageResponse(items, paged.Page, paged.PageSize, paged.TotalCount));
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
