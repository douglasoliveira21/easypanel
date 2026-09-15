using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Printers;

/// <summary>
/// Gestão do parque de impressoras do tenant do contexto (R9/R10): listagem
/// paginada com filtros, consulta por id, criação, atualização e movimentação de
/// ciclo de vida.
///
/// <para>Autorização granular (R5/R17.1): leitura → <c>printer.view</c>, criação →
/// <c>printer.create</c>, atualização → <c>printer.edit</c>, movimentação →
/// <c>printer.move</c>. O tenant vem sempre do token; cross-tenant → 404
/// (não-vazamento). Falhas de <see cref="Result"/> são mapeadas para HTTP por
/// <see cref="ErrorType"/>.</para>
/// </summary>
[ApiController]
[Route("api/v1/printers")]
public sealed class PrintersController(IPrinterService printerService) : ControllerBase
{
    private readonly IPrinterService _printerService = printerService;

    /// <summary>Lista impressoras do tenant, com filtros e paginação (R9.5).</summary>
    [HttpGet]
    [RequirePermission(Permissions.PrinterView)]
    [ProducesResponseType(typeof(PrinterPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PrinterPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? locationId = null,
        [FromQuery] string? fabricante = null,
        [FromQuery] string? modelo = null,
        [FromQuery] PrinterStatus? status = null,
        [FromQuery] bool? monitoringEnabled = null,
        CancellationToken cancellationToken = default)
    {
        var query = new PrinterQuery(
            new PageRequest(page, pageSize),
            customerId,
            locationId,
            fabricante,
            modelo,
            status,
            monitoringEnabled);

        var result = await _printerService.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var paged = result.Value;

        return Ok(new PrinterPageResponse(
            paged.Items.Select(ToResponse).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount));
    }

    /// <summary>Consulta uma impressora por id, restrita ao tenant (R9.4).</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.PrinterView)]
    [ProducesResponseType(typeof(PrinterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _printerService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Cria uma impressora vinculada ao tenant do contexto (R9.3).</summary>
    [HttpPost]
    [RequirePermission(Permissions.PrinterCreate)]
    [ProducesResponseType(typeof(PrinterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreatePrinterApiRequest request, CancellationToken cancellationToken)
    {
        var domain = new CreatePrinterRequest(
            request.CustomerId,
            request.LocationId,
            request.Fabricante,
            request.Modelo,
            request.NumeroSerie,
            request.Patrimonio,
            request.Ip,
            request.Mac,
            request.Hostname,
            request.Protocolo,
            request.Porta,
            request.MonitoringEnabled,
            request.Observacoes);

        var result = await _printerService.CreateAsync(domain, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var response = ToResponse(result.Value);
        return CreatedAtAction(nameof(Get), new { id = response.Id }, response);
    }

    /// <summary>Atualiza uma impressora do próprio tenant (R9).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.PrinterEdit)]
    [ProducesResponseType(typeof(PrinterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePrinterApiRequest request,
        CancellationToken cancellationToken)
    {
        var domain = new UpdatePrinterRequest(
            request.Fabricante,
            request.Modelo,
            request.NumeroSerie,
            request.Patrimonio,
            request.Ip,
            request.Mac,
            request.Hostname,
            request.Protocolo,
            request.Porta,
            request.MonitoringEnabled,
            request.Observacoes);

        var result = await _printerService.UpdateAsync(id, domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    /// <summary>Movimenta/gerencia ciclo de vida de uma impressora (R10).</summary>
    [HttpPost("{id:guid}/move")]
    [RequirePermission(Permissions.PrinterMove)]
    [ProducesResponseType(typeof(PrinterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Move(
        Guid id,
        MovePrinterApiRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _printerService
            .MoveAsync(id, request.Operation, request.ToLocationId, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static PrinterResponse ToResponse(PrinterDto d) => new(
        d.Id,
        d.TenantId,
        d.CustomerId,
        d.LocationId,
        d.Fabricante,
        d.Modelo,
        d.NumeroSerie,
        d.Patrimonio,
        d.Ip,
        d.Mac,
        d.Hostname,
        d.Protocolo,
        d.Porta,
        d.Status,
        d.MonitoringEnabled,
        d.InstalledAt,
        d.Observacoes,
        d.CreatedAt,
        d.UpdatedAt);

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
