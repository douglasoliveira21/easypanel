using EasyPanel.Modules.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Client;

/// <summary>Uma leitura de contador submetida pelo agente.</summary>
public sealed record SubmittedCounterDto(string CounterType, string? CounterTypeLabel, long Value);

/// <summary>Uma leitura de nível de suprimento submetida pelo agente (Fase 4).</summary>
public sealed record SubmittedSupplyDto(string Label, int Percent);

/// <summary>Submissão de coleta do agente (R12/R13; suprimento — Fase 4/R2).</summary>
public sealed record ClientCollectionRequest(
    string IdempotencyKey,
    Guid? PrinterId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Success,
    string? Errors,
    IReadOnlyList<SubmittedCounterDto> Counters,
    string? RawStatus,
    IReadOnlyList<SubmittedSupplyDto>? Supplies = null);

/// <summary>Confirmação de ingestão retornada ao agente (R13.4/R14.1).</summary>
public sealed record IngestionAckResponse(Guid CollectionId, bool Duplicate);

/// <summary>
/// Endpoints de ingestão de coletas do agente (R12/R13/R14). Autenticados pelo
/// esquema <see cref="ClientAuthConstants.SchemeName"/>. Ambos exigem
/// <c>IdempotencyKey</c>; o reenvio da mesma chave retorna o ack equivalente sem
/// duplicar dados (R13.4). O ack é imediato, sem aguardar a persistência final
/// (R14.1) — o processamento ocorre no worker.
/// </summary>
[ApiController]
[Route("api/v1/client")]
[Authorize(AuthenticationSchemes = ClientAuthConstants.SchemeName)]
public sealed class ClientIngestionController(IIngestionService ingestionService) : ControllerBase
{
    private readonly IIngestionService _ingestionService = ingestionService;

    /// <summary>Recebe uma coleta de contadores (R12/R13).</summary>
    [HttpPost("collect")]
    [ProducesResponseType(typeof(IngestionAckResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Collect(ClientCollectionRequest request, CancellationToken ct) =>
        SubmitAsync(request, ct);

    /// <summary>Alias de upload de coleta (mesma semântica idempotente — R13).</summary>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(IngestionAckResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> Upload(ClientCollectionRequest request, CancellationToken ct) =>
        SubmitAsync(request, ct);

    private async Task<IActionResult> SubmitAsync(ClientCollectionRequest request, CancellationToken ct)
    {
        var submission = new ClientCollectionSubmission(
            request.IdempotencyKey,
            request.PrinterId,
            request.StartedAt,
            request.FinishedAt,
            request.Success,
            request.Errors,
            request.Counters
                .Select(c => new SubmittedCounter(c.CounterType, c.CounterTypeLabel, c.Value))
                .ToList(),
            request.RawStatus,
            request.Supplies?.Select(s => new SubmittedSupply(s.Label, s.Percent)).ToList());

        var result = await _ingestionService.SubmitAsync(submission, ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Unauthorized();
        }

        return Accepted(new IngestionAckResponse(result.Value.CollectionId, result.Value.Duplicate));
    }
}
