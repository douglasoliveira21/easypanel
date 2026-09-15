namespace EasyPanel.Modules.Identity;

/// <summary>
/// Par de tokens emitido ao concluir uma autenticação bem-sucedida ou uma
/// renovação (R2.1, R2.4).
///
/// Transporta o JWT de acesso de curta duração e o refresh token opaco (valor
/// bruto, não persistido — o banco guarda apenas seu hash), acompanhados de seus
/// respectivos instantes de expiração. É o contrato de saída do
/// <see cref="IAuthService"/>; a camada de API o projeta em um DTO próprio, sem
/// expor entidades de persistência (R12.1).
/// </summary>
/// <param name="AccessToken">JWT de acesso assinado (compact JWS), expiração ≤ 15 min (R2.6).</param>
/// <param name="RefreshToken">Valor bruto opaco do refresh token, a ser entregue ao cliente.</param>
/// <param name="AccessTokenExpiresAt">Momento de expiração do JWT de acesso (UTC).</param>
/// <param name="RefreshTokenExpiresAt">Momento de expiração do refresh token (UTC).</param>
public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);
