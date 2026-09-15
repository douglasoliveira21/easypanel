using System.Security.Claims;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Auth;

/// <summary>
/// Endpoints de autenticação de sessão (R2): login, renovação (refresh) e logout.
///
/// Login e refresh são anônimos (o refresh se autentica pelo próprio token
/// opaco). Todos usam DTOs distintos das entidades (R12.1) e mapeiam falhas de
/// autenticação para HTTP 401 diretamente a partir do <see cref="Result"/>
/// devolvido pelo <see cref="IAuthService"/> — o tratamento centralizado de erros
/// (ProblemDetails) é introduzido na tarefa 9.1.
///
/// As respostas de credenciais inválidas são genéricas (R2.2) e não revelam a
/// causa; o estado de bloqueio (R4.4) usa o mesmo status 401, distinguível apenas
/// pelo código do problema no corpo.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    private readonly IAuthService _authService = authService;

    /// <summary>
    /// Nome curto do claim de subject (Id do usuário) embutido no JWT. Preservado
    /// literalmente porque o esquema Bearer usa <c>MapInboundClaims = false</c>.
    /// </summary>
    private const string SubClaimType = "sub";

    /// <summary>
    /// Autentica por email e senha. Em caso de sucesso retorna 200: com o par de
    /// tokens quando o usuário não exige MFA (R2.1), ou com um desafio de segundo
    /// fator (<see cref="MfaChallengeResponse"/>, sem tokens) quando o usuário tem
    /// MFA habilitado (R2.9). Credenciais inválidas, conta inativa ou bloqueada
    /// retornam 401 (R2.2, R4.4, R7.5).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MfaChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _authService
            .LoginAsync(request.Email, request.Password, ip, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Unauthorized(ToProblem(result.Error));
        }

        var login = result.Value;

        // Usuário com MFA: 200 com o desafio, sem tokens (R2.9). Usuário sem MFA:
        // 200 com o par de tokens, comportamento inalterado (R2.10).
        return login.MfaRequired
            ? Ok(ToChallengeResponse(login.Challenge!))
            : Ok(ToResponse(login.Tokens!));
    }

    /// <summary>
    /// Completa a autenticação de segundo fator (R2.9): valida o <c>mfa_ticket</c>
    /// e o código e, em caso de sucesso, retorna 200 com o par de tokens. Ticket
    /// inválido/expirado ou código rejeitado retornam 401.
    /// </summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyMfa(VerifyMfaRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService
            .VerifyMfaAsync(request.MfaTicket, request.Code, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : Unauthorized(ToProblem(result.Error));
    }

    /// <summary>
    /// Renova a sessão a partir de um refresh token válido, rotacionando-o (R2.4).
    /// Token inválido/expirado/revogado retorna 401 (R2.5).
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService
            .RefreshAsync(request.RefreshToken, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? Ok(ToResponse(result.Value))
            : Unauthorized(ToProblem(result.Error));
    }

    /// <summary>
    /// Encerra a sessão associada ao refresh token informado, revogando-o (R2.3).
    /// Idempotente: retorna 204 mesmo para tokens desconhecidos.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request.RefreshToken, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Inicia a recuperação de senha (R3.1). Responde <b>sempre</b> 204, exista ou
    /// não a conta, para não revelar a existência do email (R3.2). Quando o email
    /// pertence a uma conta ativa, um token de redefinição de uso único (≤ 60 min)
    /// é gerado e entregue fora de banda.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        // O resultado é sempre sucesso (não-vazamento — R3.2).
        await _authService.ForgotPasswordAsync(request.Email, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Redefine a senha a partir de um token válido (R3.3). Em sucesso retorna 204 e
    /// revoga todas as sessões do usuário (R3.4). Token inválido/expirado ou senha
    /// fora da política retornam 400 (R3.7), sem revelar a existência da conta além
    /// do que o token pressupõe.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService
            .ResetPasswordAsync(request.Email, request.Token, request.NewPassword, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? NoContent()
            : BadRequest(ToBadRequestProblem(result.Error));
    }

    /// <summary>
    /// Altera a senha do usuário autenticado (R3.5). O usuário é identificado pelo
    /// claim <c>sub</c> do token, nunca pelo corpo. Em sucesso retorna 204 e revoga
    /// todas as sessões do usuário (R3.4). Senha atual incorreta ou nova senha fora
    /// da política retornam 400 (R3.6/R3.7). Requisição não autenticada → 401.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        // O identificador do usuário vem do claim `sub` do JWT (R6.3): jamais do
        // corpo da requisição. MapInboundClaims=false preserva o nome curto `sub`,
        // e o NameClaimType do esquema Bearer aponta para `sub` — então tanto
        // User.Identity.Name quanto o claim `sub` resolvem o Id do usuário.
        var subject = User.FindFirstValue(SubClaimType);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var result = await _authService
            .ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? NoContent()
            : BadRequest(ToBadRequestProblem(result.Error));
    }

    private static TokenResponse ToResponse(TokenPair pair) =>
        new(
            pair.AccessToken,
            pair.RefreshToken,
            pair.AccessTokenExpiresAt,
            pair.RefreshTokenExpiresAt);

    private static MfaChallengeResponse ToChallengeResponse(MfaChallenge challenge) =>
        new(
            MfaRequired: true,
            challenge.MfaTicket,
            challenge.ExpiresAt);

    private static ProblemDetails ToProblem(Error error) =>
        new()
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Não autorizado",
            Detail = error.Message,
            Type = error.Code,
        };

    private static ProblemDetails ToBadRequestProblem(Error error) =>
        new()
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Requisição inválida",
            Detail = error.Message,
            Type = error.Code,
        };
}
