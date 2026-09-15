using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Erros esperados dos fluxos de autenticação (R2, R4, R7.5), expostos como
/// valores estáveis para uso pelo <see cref="IAuthService"/> e mapeamento na
/// camada de API.
///
/// Todos os erros de credencial são <see cref="ErrorType.NotFound"/>-agnósticos
/// quanto à causa: a mensagem é genérica e não revela se o email existe, se a
/// senha está incorreta ou se a conta está inativa (R2.2, R7.5). A API mapeia
/// estes erros para HTTP 401.
/// </summary>
public static class AuthErrors
{
    /// <summary>
    /// Credenciais inválidas (email desconhecido, senha incorreta ou conta
    /// inativa). Mensagem genérica que não revela qual campo está incorreto
    /// (R2.2, R7.5). Mapeado para HTTP 401.
    /// </summary>
    public static readonly Error InvalidCredentials = Error.Validation(
        "auth.invalid_credentials",
        "Credenciais inválidas.");

    /// <summary>
    /// Conta temporariamente bloqueada por excesso de falhas consecutivas (R4.3,
    /// R4.4). Diferente de <see cref="InvalidCredentials"/> porque R4.4 exige
    /// informar o estado de bloqueio. Mapeado para HTTP 401.
    /// </summary>
    public static readonly Error LockedOut = Error.Validation(
        "auth.locked_out",
        "Conta temporariamente bloqueada por excesso de tentativas. Tente novamente mais tarde.");

    /// <summary>
    /// Refresh token inválido, expirado ou revogado, ou usuário associado
    /// inexistente/inativo (R2.5). Mapeado para HTTP 401.
    /// </summary>
    public static readonly Error InvalidRefreshToken = Error.NotFound(
        "auth.refresh_token.invalid",
        "Refresh token inválido, expirado ou revogado.");

    /// <summary>
    /// Sinaliza que a autenticação exige um segundo fator antes de emitir os
    /// tokens da sessão (R2.9). Informativo — o desafio propriamente dito
    /// (<see cref="MfaChallenge"/>) trafega no <see cref="LoginResult"/>; este erro
    /// existe para os casos em que a camada de API precise sinalizar a exigência
    /// de MFA por meio do padrão <see cref="Result"/>.
    /// </summary>
    public static readonly Error MfaRequired = Error.Validation(
        "auth.mfa.required",
        "Autenticação de segundo fator obrigatória.");

    /// <summary>
    /// <c>mfa_ticket</c> inválido, expirado ou de propósito incorreto apresentado
    /// na verificação de segundo fator (R2.9). Mapeado para HTTP 401.
    /// </summary>
    public static readonly Error InvalidMfaTicket = Error.NotFound(
        "auth.mfa.ticket.invalid",
        "Ticket de segundo fator inválido ou expirado.");

    /// <summary>
    /// Código de segundo fator rejeitado pelo validador (R2.9). Mensagem genérica
    /// que não revela se o ticket ou o código estava incorreto. Mapeado para HTTP 401.
    /// </summary>
    public static readonly Error InvalidMfaCode = Error.Validation(
        "auth.mfa.code.invalid",
        "Segundo fator inválido.");

    /// <summary>
    /// Token de redefinição de senha inválido, expirado ou já utilizado, ou email
    /// não associado a uma conta (R3.3). A mensagem é genérica: como a redefinição
    /// só progride com um token válido, não revela existência de conta além do que
    /// o próprio token já pressupõe. Mapeado para HTTP 400.
    /// </summary>
    public static readonly Error InvalidResetToken = Error.Validation(
        "auth.reset_token.invalid",
        "Token de redefinição inválido ou expirado.");

    /// <summary>
    /// Senha atual incorreta informada na alteração de senha autenticada (R3.6).
    /// Mapeado para HTTP 400.
    /// </summary>
    public static readonly Error IncorrectCurrentPassword = Error.Validation(
        "auth.password.current_incorrect",
        "Senha atual incorreta.");

    /// <summary>
    /// Nova senha não atende à política de senhas (mín. 8 caracteres com
    /// maiúscula, minúscula, dígito e caractere especial — R3.7). Mapeado para
    /// HTTP 400.
    /// </summary>
    public static readonly Error WeakPassword = Error.Validation(
        "auth.password.weak",
        "A nova senha não atende à política de senhas.");
}
