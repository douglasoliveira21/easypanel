using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Inventory;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Inventory;

/// <summary>
/// Cadastro do catálogo de itens de estoque do tenant do contexto (Fase 5 — R1).
/// Autorização granular: leitura → <c>estoque.view</c>; criação/edição →
/// <c>estoque.manage</c>. O tenant vem sempre do token; cross-tenant → 404.
/// </summary>
[ApiController]
[Route("api/v1/inventory-items")]
public sealed class InventoryItemsController(IInventoryItemService itemService) : ControllerBase
{
    private readonly IInventoryItemService _itemService = itemService;

    /// <summary>Lista itens do tenant, com busca e paginação (R1.4).</summary>
    [HttpGet]
    [RequirePermission(Permissions.EstoqueView)]
    [ProducesResponseType(typeof(InventoryItemPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InventoryItemPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = new InventoryItemQuery(new PageRequest(page, pageSize), search, isActive);
        var result = await _itemService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new InventoryItemPageResponse(
            paged.Items.Select(ToResponse).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount));
    }

    /// <summary>Consulta um item por id, restrito ao tenant (R1.4).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.EstoqueView)]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _itemService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Cria um item vinculado ao tenant do contexto (R1.2).</summary>
    [HttpPost]
    [RequirePermission(Permissions.EstoqueManage)]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateInventoryItemApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new CreateInventoryItemRequest(
            request.Name, request.Sku, request.Unit, request.SupplyLabel, request.Observations);

        var result = await _itemService.CreateAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>Atualiza um item do próprio tenant, incluindo ativação/desativação (R1, R1.5).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.EstoqueManage)]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateInventoryItemApiRequest request,
        CancellationToken cancellationToken)
    {
        var domain = new UpdateInventoryItemRequest(
            request.Name, request.Sku, request.Unit, request.SupplyLabel, request.IsActive, request.Observations);

        var result = await _itemService.UpdateAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static InventoryItemResponse ToResponse(InventoryItemDto d) => new(
        d.Id, d.TenantId, d.Name, d.Sku, d.Unit, d.SupplyLabel, d.IsActive, d.Observations, d.CreatedAt, d.UpdatedAt);

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
