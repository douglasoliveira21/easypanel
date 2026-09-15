using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Inventory;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Inventory;

/// <summary>
/// Registro e histórico de movimentações de estoque do tenant do contexto
/// (Fase 5 — R2/R3.3/R4.3). Registrar exige <c>estoque.manage</c>; consultar
/// histórico exige <c>estoque.view</c>. Histórico por cursor pagination (grandes
/// volumes). Tudo restrito ao tenant do contexto (cross-tenant → 404); saída sem
/// saldo suficiente → 409; ajuste sem justificativa → 400.
/// </summary>
[ApiController]
public sealed class InventoryMovementsController(IInventoryMovementService movementService) : ControllerBase
{
    private const int DefaultPageSize = 50;

    private readonly IInventoryMovementService _movementService = movementService;

    /// <summary>Registra uma movimentação (Entrada/Saída/Ajuste) — R2.1.</summary>
    [HttpPost("api/v1/inventory-movements")]
    [RequirePermission(Permissions.EstoqueManage)]
    [ProducesResponseType(typeof(InventoryMovementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterInventoryMovementApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new RegisterInventoryMovementRequest(
            request.ItemId,
            request.LocationId,
            request.Type,
            request.AdjustmentDirection,
            request.Quantity,
            request.PrinterId,
            request.Reason);

        var result = await _movementService.RegisterAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Histórico de movimentações de um Item, com filtro opcional por Local, por cursor pagination (R3.3).</summary>
    [HttpGet("api/v1/inventory-items/{itemId:guid}/movements")]
    [RequirePermission(Permissions.EstoqueView)]
    [ProducesResponseType(typeof(InventoryCursorPageResponse<InventoryMovementResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListByItem(
        Guid itemId,
        [FromQuery] Guid? locationId = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _movementService
            .ListHistoryAsync(itemId, locationId, cursor, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new InventoryCursorPageResponse<InventoryMovementResponse>(
            page.Items.Select(ToResponse).ToList(),
            page.NextCursor));
    }

    /// <summary>Movimentações que referenciaram uma Impressora, por cursor pagination (R4.3).</summary>
    [HttpGet("api/v1/printers/{printerId:guid}/inventory-movements")]
    [RequirePermission(Permissions.EstoqueView)]
    [ProducesResponseType(typeof(InventoryCursorPageResponse<InventoryMovementResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListByPrinter(
        Guid printerId,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _movementService
            .ListByPrinterAsync(printerId, cursor, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new InventoryCursorPageResponse<InventoryMovementResponse>(
            page.Items.Select(ToResponse).ToList(),
            page.NextCursor));
    }

    private static InventoryMovementResponse ToResponse(InventoryMovementDto d) => new(
        d.Id, d.ItemId, d.LocationId, d.Type, d.AdjustmentDirection, d.Quantity, d.PrinterId, d.Reason, d.ActorUserId, d.OccurredAt);

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
