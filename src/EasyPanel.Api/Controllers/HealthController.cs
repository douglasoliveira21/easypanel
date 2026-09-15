using EasyPanel.Infrastructure.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyPanel.Api.Controllers;

/// <summary>
/// Endpoints de verificação de saúde da plataforma (R1.3, R1.5). Ambos são
/// anônimos, conforme a tabela de endpoints do design.
///
/// <list type="bullet">
///   <item>
///     <c>/health/live</c> — sonda de liveness simples: indica que o processo
///     está de pé e respondendo, sem avaliar dependências externas.
///   </item>
///   <item>
///     <c>/health/ready</c> — sonda de prontidão: avalia as dependências
///     obrigatórias (PostgreSQL, Redis e S3/MinIO). Se qualquer uma estiver
///     indisponível, retorna HTTP 503 (não saudável) (R1.5).
///   </item>
/// </list>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(HealthCheckService healthCheckService) : ControllerBase
{
    /// <summary>
    /// Sonda de liveness. Não verifica dependências; retorna 200 enquanto o
    /// processo estiver operacional.
    /// </summary>
    [HttpGet("live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Live() => Ok(new HealthResponse(
        HealthStatus.Healthy.ToString(),
        new Dictionary<string, string>()));

    /// <summary>
    /// Sonda de prontidão. Executa apenas os checks marcados com a tag
    /// <see cref="HealthChecksServiceCollectionExtensions.ReadyTag"/> (PostgreSQL,
    /// Redis e S3/MinIO). Retorna 200 se todos saudáveis, ou 503 se qualquer
    /// dependência obrigatória estiver indisponível (R1.5).
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var report = await healthCheckService
            .CheckHealthAsync(
                registration => registration.Tags.Contains(
                    HealthChecksServiceCollectionExtensions.ReadyTag),
                cancellationToken)
            .ConfigureAwait(false);

        var entries = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString());

        var response = new HealthResponse(report.Status.ToString(), entries);

        var statusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        return StatusCode(statusCode, response);
    }
}

/// <summary>
/// Corpo de resposta dos endpoints de saúde: estado agregado e o estado por
/// dependência verificada.
/// </summary>
public sealed record HealthResponse(string Status, IReadOnlyDictionary<string, string> Checks);
