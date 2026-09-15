using System.ComponentModel.DataAnnotations;

namespace EasyPanel.Api.Controllers.Auth;

/// <summary>
/// Corpo da requisição de login (R2.1). DTO distinto das entidades de
/// persistência (R12.1).
///
/// As anotações de validação são aplicadas aos parâmetros do construtor
/// primário (alvo padrão em records posicionais), como exige o model binder do
/// ASP.NET Core: entrada inválida → HTTP 400 (R12.2).
/// </summary>
/// <param name="Email">Email do usuário.</param>
/// <param name="Password">Senha em texto claro (validada e nunca logada).</param>
public sealed record LoginRequest(
    [Required] string Email,
    [Required] string Password);

/// <summary>
/// Corpo da requisição de renovação de sessão (R2.4).
/// </summary>
/// <param name="RefreshToken">Valor bruto opaco do refresh token.</param>
public sealed record RefreshRequest(
    [Required] string RefreshToken);

/// <summary>
/// Corpo da requisição de logout (R2.3).
/// </summary>
/// <param name="RefreshToken">Valor bruto opaco do refresh token da sessão a encerrar.</param>
public sealed record LogoutRequest(
    [Required] string RefreshToken);

/// <summary>
/// Resposta de autenticação/renovação bem-sucedida: par de tokens e suas
/// expirações. Projeção do <see cref="EasyPanel.Modules.Identity.TokenPair"/> sem
/// expor entidades (R12.1).
/// </summary>
/// <param name="AccessToken">JWT de acesso assinado (Bearer).</param>
/// <param name="RefreshToken">Valor bruto opaco do refresh token.</param>
/// <param name="AccessTokenExpiresAt">Expiração do JWT de acesso (UTC).</param>
/// <param name="RefreshTokenExpiresAt">Expiração do refresh token (UTC).</param>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>
/// Resposta de login quando o usuário exige segundo fator (R2.9). Não contém
/// tokens de sessão: apenas sinaliza a exigência de MFA e devolve o
/// <c>mfa_ticket</c> de curta duração que deve ser reapresentado, junto do código,
/// ao endpoint <c>/api/v1/auth/mfa/verify</c>. Projeção do
/// <see cref="EasyPanel.Modules.Identity.MfaChallenge"/> (R12.1).
/// </summary>
/// <param name="MfaRequired">Sempre <c>true</c>; discrimina esta resposta da de tokens.</param>
/// <param name="MfaTicket">Ticket de curta duração que identifica o desafio pendente.</param>
/// <param name="ExpiresAt">Expiração do ticket (UTC).</param>
public sealed record MfaChallengeResponse(
    bool MfaRequired,
    string MfaTicket,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Corpo da requisição de verificação de segundo fator (R2.9). DTO distinto das
/// entidades (R12.1).
/// </summary>
/// <param name="MfaTicket">Ticket de segundo fator recebido na resposta de login.</param>
/// <param name="Code">Código do segundo fator informado pelo usuário.</param>
public sealed record VerifyMfaRequest(
    [Required] string MfaTicket,
    [Required] string Code);

/// <summary>
/// Corpo da requisição de recuperação de senha (R3.1/R3.2). DTO distinto das
/// entidades (R12.1). A resposta é idêntica para email cadastrado e não cadastrado
/// (não-vazamento — R3.2).
/// </summary>
/// <param name="Email">Email da conta para a qual solicitar a redefinição.</param>
public sealed record ForgotPasswordRequest(
    [Required] string Email);

/// <summary>
/// Corpo da requisição de redefinição de senha via token (R3.3). DTO distinto das
/// entidades (R12.1).
/// </summary>
/// <param name="Email">Email da conta cuja senha será redefinida.</param>
/// <param name="Token">Token de redefinição de uso único recebido fora de banda.</param>
/// <param name="NewPassword">Nova senha, sujeita à política de senhas (R3.7).</param>
public sealed record ResetPasswordRequest(
    [Required] string Email,
    [Required] string Token,
    [Required] string NewPassword);

/// <summary>
/// Corpo da requisição de alteração de senha autenticada (R3.5/R3.6). DTO distinto
/// das entidades (R12.1). O usuário é identificado pelo claim <c>sub</c> do token,
/// não pelo corpo.
/// </summary>
/// <param name="CurrentPassword">Senha atual, para confirmação (R3.6).</param>
/// <param name="NewPassword">Nova senha, sujeita à política de senhas (R3.7).</param>
public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required] string NewPassword);
