namespace EasyPanel.Modules.Identity;

/// <summary>
/// Abstração do segundo fator de autenticação (R2.9). O fluxo de verificação de
/// MFA delega a este validador a decisão de aceitar (ou não) o código apresentado
/// pelo usuário após o desafio de login.
///
/// <para><b>Seam da Fase 1.</b> Nenhum provedor concreto de TOTP existe ainda; a
/// implementação padrão registrada nesta fase é <b>conservadora e falha fechada</b>
/// (sempre rejeita), de modo que um usuário com MFA habilitado <i>não consegue</i>
/// obter tokens até que um validador real seja registrado numa fase futura. Isso
/// mantém R2.9 honesto (sem bypass) mesmo com a estrutura pronta.</para>
///
/// <para>A enumeração/provisionamento de TOTP e um validador real serão
/// registrados em fase posterior, substituindo a implementação padrão sem alterar
/// este contrato nem o fluxo de login/verificação.</para>
/// </summary>
public interface IMfaValidator
{
    /// <summary>
    /// Valida o código de segundo fator apresentado pelo usuário.
    /// </summary>
    /// <param name="user">Usuário que está completando o desafio de MFA.</param>
    /// <param name="code">Código do segundo fator informado (ex.: TOTP).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o código for válido; caso contrário, <c>false</c>.</returns>
    Task<bool> ValidateAsync(ApplicationUser user, string code, CancellationToken ct);
}
