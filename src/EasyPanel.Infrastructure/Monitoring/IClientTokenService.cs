using EasyPanel.Modules.Monitoring;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Emissão e validação dos tokens próprios do agente Windows (R4.1/R4.4).
///
/// Reutiliza a chave de assinatura das <see cref="Security.JwtOptions"/>, porém
/// com um audience dedicado (<see cref="ClientAuthConstants.Audience"/>) e claims
/// de identidade do agente (client/tenant/local). O token de acesso é curto
/// (≤ 15 min); a renovação usa um refresh opaco hasheado, análogo ao dos usuários.
/// </summary>
public interface IClientTokenService
{
    /// <summary>
    /// Emite um par de tokens (acesso curto + refresh opaco) para o agente
    /// identificado, embutindo tenant/local nos claims (R4.1).
    /// </summary>
    Task<ClientTokenPair> IssueAsync(
        Guid clientId,
        Guid tenantId,
        Guid locationId,
        CancellationToken ct);

    /// <summary>
    /// Igual a <see cref="IssueAsync"/>, porém persiste o refresh no
    /// <paramref name="context"/> informado — usado por operações confiáveis que
    /// gravam fora do <c>AppDbContext</c> scoped da requisição (ex.: registro de
    /// agentes com contexto de sistema).
    /// </summary>
    Task<ClientTokenPair> IssueInAsync(
        Persistence.AppDbContext context,
        Guid clientId,
        Guid tenantId,
        Guid locationId,
        CancellationToken ct);

    /// <summary>
    /// Valida o refresh opaco e, se ativo, rotaciona-o emitindo um novo par (R4.4).
    /// Retorna <c>null</c> se o token for desconhecido/expirado/revogado (→ 401).
    /// </summary>
    Task<ClientTokenPair?> RefreshAsync(string refreshToken, CancellationToken ct);
}
