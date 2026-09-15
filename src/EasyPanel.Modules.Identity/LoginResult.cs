namespace EasyPanel.Modules.Identity;

/// <summary>
/// Resultado discriminado de um login bem-sucedido em nível de credenciais
/// (R2.9, R2.10). Representa <b>exatamente um</b> de dois desfechos:
/// <list type="bullet">
///   <item><b>Tokens emitidos</b> — o usuário não exige segundo fator
///   (<see cref="ApplicationUser.MfaEnabled"/> falso). <see cref="Tokens"/> traz o
///   <see cref="TokenPair"/> e <see cref="MfaRequired"/> é <c>false</c>. Este é o
///   fluxo idêntico ao anterior à Fase MFA — usuários sem MFA não têm o
///   comportamento alterado (R2.10).</item>
///   <item><b>Desafio de MFA</b> — o usuário exige segundo fator
///   (<c>MfaEnabled</c> verdadeiro). <see cref="Challenge"/> traz o
///   <see cref="MfaChallenge"/> (ticket de curta duração), <see cref="Tokens"/> é
///   <c>null</c> e <see cref="MfaRequired"/> é <c>true</c>. Nenhum token de sessão
///   é emitido nesta etapa (R2.9).</item>
/// </list>
///
/// <para>Modelado como um record com fábricas estáticas (<see cref="ForTokens"/> e
/// <see cref="ForMfaChallenge"/>) em vez de uma hierarquia selada, para manter o
/// contrato simples e serializável, garantindo por construção a exclusividade
/// mútua entre <see cref="Tokens"/> e <see cref="Challenge"/>.</para>
/// </summary>
public sealed record LoginResult
{
    private LoginResult(bool mfaRequired, TokenPair? tokens, MfaChallenge? challenge)
    {
        MfaRequired = mfaRequired;
        Tokens = tokens;
        Challenge = challenge;
    }

    /// <summary>
    /// Indica que o login exige um segundo fator: quando <c>true</c>,
    /// <see cref="Challenge"/> está preenchido e <see cref="Tokens"/> é <c>null</c>.
    /// </summary>
    public bool MfaRequired { get; }

    /// <summary>Par de tokens emitido quando o login não exige MFA; <c>null</c> caso contrário.</summary>
    public TokenPair? Tokens { get; }

    /// <summary>Desafio de segundo fator quando o login exige MFA; <c>null</c> caso contrário.</summary>
    public MfaChallenge? Challenge { get; }

    /// <summary>Cria um resultado que carrega o par de tokens emitido (fluxo sem MFA — R2.10).</summary>
    public static LoginResult ForTokens(TokenPair tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new LoginResult(mfaRequired: false, tokens: tokens, challenge: null);
    }

    /// <summary>Cria um resultado que carrega o desafio de MFA, sem emitir tokens (R2.9).</summary>
    public static LoginResult ForMfaChallenge(MfaChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return new LoginResult(mfaRequired: true, tokens: null, challenge: challenge);
    }
}
