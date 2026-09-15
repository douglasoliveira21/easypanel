using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Client;

/// <summary>Payload de heartbeat enviado pelo agente (R6.2).</summary>
public sealed record ClientHeartbeatRequest(
    string AgentVersion,
    string Hostname,
    DateTimeOffset Timestamp,
    string State,
    int PrinterCount,
    DateTimeOffset? LastCollectionAt);

/// <summary>
/// Endpoint de heartbeat do agente (R6). Autenticado pelo esquema
/// <see cref="ClientAuthConstants.SchemeName"/>: a identidade do agente
/// (client/tenant/local) vem do token, nunca do payload (R4.2/R6.3). Atualiza
/// estado/horário do agente autenticado (R6.3).
/// </summary>
[ApiController]
[Route("api/v1/client")]
[Authorize(AuthenticationSchemes = ClientAuthConstants.SchemeName)]
public sealed class ClientHeartbeatController(IHeartbeatService heartbeatService) : ControllerBase
{
    private readonly IHeartbeatService _heartbeatService = heartbeatService;

    /// <summary>Registra um heartbeat do agente autenticado (R6.3).</summary>
    [HttpPost("heartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Heartbeat(
        ClientHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _heartbeatService
            .RecordAsync(
                new HeartbeatRequest(
                    request.AgentVersion,
                    request.Hostname,
                    request.Timestamp,
                    request.State,
                    request.PrinterCount,
                    request.LastCollectionAt),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        return result.Error.Type == ErrorType.NotFound
            ? NotFound()
            : Unauthorized();
    }
}
