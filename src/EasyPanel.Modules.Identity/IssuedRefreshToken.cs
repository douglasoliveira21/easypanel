namespace EasyPanel.Modules.Identity;

/// <summary>
/// Resultado da emissão (ou rotação) de um refresh token.
///
/// Transporta o par indissociável [valor bruto, entidade persistida]: o
/// <see cref="RawToken"/> é o valor opaco que deve ser entregue ao cliente e
/// <b>nunca</b> é persistido; a <see cref="Token"/> é o registro correspondente
/// gravado (contendo apenas o hash). Este record existe porque a interface
/// <see cref="ITokenService"/> precisa devolver ambos ao chamador sem que o
/// valor bruto trafegue pela camada de persistência.
/// </summary>
/// <param name="RawToken">Valor opaco a ser entregue ao cliente (não persistido).</param>
/// <param name="Token">Registro persistido do refresh token (somente o hash).</param>
public sealed record IssuedRefreshToken(string RawToken, RefreshToken Token)
{
    /// <summary>Momento de expiração do token emitido (UTC).</summary>
    public DateTimeOffset ExpiresAt => Token.ExpiresAt;
}
