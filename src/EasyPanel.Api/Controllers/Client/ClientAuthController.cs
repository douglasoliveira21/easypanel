using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Client;

/// <summary>
/// Endpoints de autenticação própria do agente Windows (R4).
///
/// <c>token</c> autentica por <c>client_id</c> + <c>client_secret</c> e emite um
/// par de tokens; <c>refresh</c> renova o token de acesso a partir da credencial
/// de renovação. Ambos são anônimos (a identidade é estabelecida pelas
/// credenciais próprias, não por um JWT prévio). Credenciais inválidas/expiradas
/// retornam 401 (R4.3/R4.5). DTOs distintos das entidades (R12.1).
/// </summary>
[ApiController]
[Route("api/v1/client")]
public sealed class ClientAuthController(
    IClientAuthService authService,
    IClientRegistrationService registrationService) : ControllerBase
{
    private readonly IClientAuthService _authService = authService;
    private readonly IClientRegistrationService _registrationService = registrationService;

    /// <summary>
    /// Registra um agente a partir de uma chave de provisionamento de Local válida,
    /// vinculando-o ao Tenant/Local resolvidos e emitindo credenciais próprias
    /// (R3.2–R3.4). Chave inválida/expirada retorna 401 (R3.5).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ClientRegisterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register(
        ClientRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _registrationService
            .RegisterAsync(
                new RegisterClientRequest(
                    request.ProvisioningKey,
                    request.UniqueId,
                    request.Hostname,
                    request.AgentVersion),
                ip,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Unauthorized(ToProblem(result.Error));
        }

        var value = result.Value;
        return Ok(new ClientRegisterResponse(
            value.ClientId,
            value.ClientSecret,
            ToResponse(value.Tokens)));
    }

    /// <summary>Autentica o agente e emite um par de tokens (R4.1).</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ClientTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Token(ClientAuthRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService
            .AuthenticateAsync(request.ClientId, request.ClientSecret, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : Unauthorized(ToProblem(result.Error));
    }

    /// <summary>Renova o token de acesso do agente (R4.4).</summary>
    [HttpPost("token/refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ClientTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(ClientRefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService
            .RefreshAsync(request.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : Unauthorized(ToProblem(result.Error));
    }

    private static ClientTokenResponse ToResponse(ClientTokenPair pair) =>
        new(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt);

    private static ProblemDetails ToProblem(Error error) =>
        new()
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Não autorizado",
            Detail = error.Message,
            Type = error.Code,
        };
}
