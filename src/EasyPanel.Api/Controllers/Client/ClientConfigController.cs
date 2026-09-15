using EasyPanel.Modules.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Client;

/// <summary>Impressora monitorável reportada ao agente (Fase 4).</summary>
public sealed record MonitoredPrinterResponse(
    Guid PrinterId,
    string Ip,
    string? Protocolo,
    int? Porta,
    string? Fabricante);

/// <summary>Configuração operacional entregue ao agente (R15.2; suprimento — Fase 4).</summary>
public sealed record ClientConfigResponse(
    int CollectionIntervalSeconds,
    IReadOnlyList<string> DiscoveryTargets,
    IReadOnlyList<string> IgnoredPrinters,
    IReadOnlyList<MonitoredPrinterResponse> MonitoredPrinters);

/// <summary>Metadados de atualização do agente (R16.1).</summary>
public sealed record ClientUpdateResponse(
    string Version,
    string PackageUrl,
    string Sha256,
    string Signature);

/// <summary>
/// Configuração e atualização do agente (R15/R16). Autenticados pelo esquema
/// <see cref="ClientAuthConstants.SchemeName"/>: a identidade do agente resolve
/// tenant/local, restringindo os dados (R15.1). <c>update</c> retorna 404 quando
/// não há versão publicada.
/// </summary>
[ApiController]
[Route("api/v1/client")]
[Authorize(AuthenticationSchemes = ClientAuthConstants.SchemeName)]
public sealed class ClientConfigController(
    IClientConfigService configService,
    IClientUpdateService updateService) : ControllerBase
{
    private readonly IClientConfigService _configService = configService;
    private readonly IClientUpdateService _updateService = updateService;

    /// <summary>Retorna a configuração vigente do agente autenticado (R15.1).</summary>
    [HttpGet("config")]
    [ProducesResponseType(typeof(ClientConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var result = await _configService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Unauthorized();
        }

        var c = result.Value;
        var monitoredPrinters = (c.MonitoredPrinters ?? [])
            .Select(p => new MonitoredPrinterResponse(p.PrinterId, p.Ip, p.Protocolo, p.Porta, p.Fabricante))
            .ToList();

        return Ok(new ClientConfigResponse(
            c.CollectionIntervalSeconds, c.DiscoveryTargets, c.IgnoredPrinters, monitoredPrinters));
    }

    /// <summary>Retorna os metadados da versão de atualização disponível (R16.1).</summary>
    [HttpGet("update")]
    [ProducesResponseType(typeof(ClientUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUpdate(CancellationToken cancellationToken)
    {
        var result = await _updateService.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            var u = result.Value;
            return Ok(new ClientUpdateResponse(u.Version, u.PackageUrl, u.Sha256, u.Signature));
        }

        return result.Error.Type == Shared.Kernel.Results.ErrorType.NotFound
            ? NotFound()
            : Unauthorized();
    }
}
