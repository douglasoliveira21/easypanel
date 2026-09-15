using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Printers;

/// <summary>Corpo de ajuste administrativo de contador (R11.6).</summary>
public sealed record AdjustCounterApiRequest(
    CounterType CounterType,
    string? CounterTypeLabel,
    long NewValue,
    string Justification);

/// <summary>Projeção de leitura de contador na API (R11).</summary>
public sealed record CounterResponse(
    Guid Id,
    Guid PrinterId,
    DateTimeOffset Timestamp,
    CounterType CounterType,
    string? CounterTypeLabel,
    long Value,
    CounterSource Source,
    bool IsAdministrativeAdjustment);

/// <summary>Página baseada em cursor de contadores.</summary>
public sealed record CounterCursorPageResponse(IReadOnlyList<CounterResponse> Items, string? NextCursor);

/// <summary>
/// Consulta e ajuste de contadores de uma impressora (R11). Histórico por cursor
/// pagination (R11.7) exige <c>counter.view</c>; o ajuste administrativo (R11.6)
/// exige <c>counter.adjust</c>. Tudo restrito ao tenant do contexto (cross-tenant
/// → 404). Ajuste sem justificativa → 400; leitura com decréscimo → 409.
/// </summary>
[ApiController]
[Route("api/v1/printers/{printerId:guid}/counters")]
public sealed class CountersController(ICounterService counterService) : ControllerBase
{
    private readonly ICounterService _counterService = counterService;

    /// <summary>Lista o histórico de contadores por cursor pagination (R11.7).</summary>
    [HttpGet]
    [RequirePermission(Permissions.CounterView)]
    [ProducesResponseType(typeof(CounterCursorPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid printerId,
        [FromQuery] CounterType? counterType = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _counterService
            .ListAsync(printerId, counterType, cursor, pageSize, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result.Error);
        }

        var page = result.Value;
        return Ok(new CounterCursorPageResponse(
            page.Items.Select(ToResponse).ToList(),
            page.NextCursor));
    }

    /// <summary>Aplica um ajuste administrativo auditado de contador (R11.6).</summary>
    [HttpPost("adjust")]
    [RequirePermission(Permissions.CounterAdjust)]
    [ProducesResponseType(typeof(CounterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Adjust(
        Guid printerId,
        AdjustCounterApiRequest request,
        CancellationToken cancellationToken)
    {
        var domain = new CounterAdjustmentRequest(
            printerId,
            request.CounterType,
            request.CounterTypeLabel,
            request.NewValue,
            request.Justification);

        var result = await _counterService.AdjustAsync(domain, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Ok(ToResponse(result.Value)) : MapFailure(result.Error);
    }

    private static CounterResponse ToResponse(PrinterCounterDto d) => new(
        d.Id,
        d.PrinterId,
        d.Timestamp,
        d.CounterType,
        d.CounterTypeLabel,
        d.Value,
        d.Source,
        d.IsAdministrativeAdjustment);

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
