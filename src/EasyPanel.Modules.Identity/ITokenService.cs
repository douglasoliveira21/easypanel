using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Contrato de emissão e ciclo de vida de tokens de autenticação (R2).
///
/// <para><b>JWT de acesso.</b> Curta duração (≤ 15 min — R2.6), assinado. Carrega
/// os claims <c>sub</c> (Id do usuário), <c>tenant_id</c> (quando houver),
/// <c>jti</c> (identificador único do token), os papéis do usuário e um claim
/// <c>perm_version</c>.</para>
///
/// <para><b>Decisão de projeto — permissões fora do JWT.</b> Embora
/// <see cref="CreateAccessToken"/> receba a lista de permissões por conformidade
/// com o contrato do design, o token <i>não</i> embute a lista completa: carrega
/// apenas os papéis e um <c>perm_version</c>. As permissões efetivas são
/// resolvidas por papel (cache no Redis) na avaliação de autorização (tarefa 4.2),
/// mantendo o token pequeno e permitindo revogação/atualização de papel sem
/// reemissão (R5.5). O parâmetro <c>permissions</c> é preservado na assinatura
/// para não quebrar o contrato e para uso futuro (ex.: derivar o
/// <c>perm_version</c>).</para>
///
/// <para><b>Refresh token.</b> Valor opaco aleatório (256 bits) devolvido em bruto
/// ao chamador, mas armazenado <b>apenas hasheado</b> (SHA-256). A cada uso é
/// rotacionado (R2.4): o token corrente é revogado e um novo é emitido. Tokens
/// expirados, revogados ou desconhecidos são recusados (R2.5).</para>
///
/// <para><b>Desvio de contrato documentado.</b> O design esboça
/// <c>IssueRefreshTokenAsync</c>/<c>ValidateAndRotateAsync</c> retornando a
/// entidade <see cref="RefreshToken"/>. Como o valor bruto do token não é
/// persistido (só o hash), a entidade sozinha não permite devolvê-lo ao cliente.
/// Por isso ambos retornam <see cref="IssuedRefreshToken"/> (par valor bruto +
/// entidade), preservando os nomes dos métodos e a semântica.</para>
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Cria um JWT de acesso assinado para o usuário, com expiração ≤ 15 min
    /// (R2.6). Embute <c>sub</c>, <c>tenant_id</c> (quando o usuário tem tenant),
    /// <c>jti</c>, os papéis e <c>perm_version</c>. A lista <paramref name="permissions"/>
    /// não é embutida integralmente (ver decisão de projeto na documentação da
    /// interface).
    /// </summary>
    /// <param name="user">Usuário autenticado para quem o token é emitido.</param>
    /// <param name="roles">Papéis do usuário, embutidos como claims de papel.</param>
    /// <param name="permissions">
    /// Permissões efetivas do usuário; não são embutidas no token (mantidas na
    /// assinatura por conformidade de contrato e uso futuro).
    /// </param>
    /// <returns>O JWT de acesso serializado (compact JWS).</returns>
    string CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions);

    /// <summary>
    /// Emite e persiste um novo refresh token para o usuário, devolvendo o valor
    /// bruto (a ser entregue ao cliente) junto do registro persistido (que guarda
    /// apenas o hash).
    /// </summary>
    Task<IssuedRefreshToken> IssueRefreshTokenAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Valida o valor bruto de um refresh token e, se ativo, o rotaciona: revoga
    /// o registro corrente, emite um sucessor e o retorna (R2.4). Tokens
    /// expirados, revogados ou desconhecidos produzem uma falha (R2.5), que a
    /// camada de API traduz em HTTP 401.
    /// </summary>
    Task<Result<IssuedRefreshToken>> ValidateAndRotateAsync(string token, CancellationToken ct);

    /// <summary>
    /// Revoga todos os refresh tokens ativos do usuário (logout global, troca de
    /// senha, desativação). Tokens já revogados/expirados são ignorados.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Revoga apenas o refresh token correspondente ao <paramref name="rawToken"/>
    /// informado (logout de sessão única — R2.3). É idempotente e silencioso: um
    /// token desconhecido, expirado ou já revogado é ignorado, sem sinalizar seu
    /// estado ao chamador (não-vazamento de existência).
    /// </summary>
    /// <param name="rawToken">Valor bruto opaco do refresh token a revogar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task RevokeAsync(string rawToken, CancellationToken ct);
}
