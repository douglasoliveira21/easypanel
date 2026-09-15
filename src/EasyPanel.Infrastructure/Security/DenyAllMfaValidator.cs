using EasyPanel.Modules.Identity;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação padrão de <see cref="IMfaValidator"/> na Fase 1: <b>falha
/// fechada</b> (R2.9). Nenhum provedor de TOTP existe ainda, portanto este
/// validador rejeita <i>qualquer</i> código apresentado.
///
/// <para><b>Motivo de segurança.</b> Ao rejeitar por padrão em vez de aceitar,
/// um usuário com <see cref="ApplicationUser.MfaEnabled"/> não consegue obter
/// tokens de sessão enquanto um validador real não for registrado — a plataforma
/// não fica com um bypass de segundo fator. A estrutura do fluxo (login →
/// desafio → verificação) fica pronta, mas nenhum usuário tem MFA habilitado por
/// padrão na Fase 1, de modo que o fluxo dos demais usuários permanece inalterado
/// (R2.10).</para>
///
/// <para>Uma fase futura registrará um validador de TOTP real substituindo este,
/// sem alterar o contrato nem o fluxo de login/verificação.</para>
/// </summary>
public sealed class DenyAllMfaValidator : IMfaValidator
{
    /// <inheritdoc />
    public Task<bool> ValidateAsync(ApplicationUser user, string code, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Falha fechada: sem provedor de segundo fator na Fase 1, nenhum código é
        // aceito (R2.9). Não há bypass.
        _ = user;
        _ = code;
        return Task.FromResult(false);
    }
}
