namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Seam reutilizável para registrar, na trilha de auditoria, uma tentativa de
/// acesso a dados entre tenants que foi <b>recusada</b> (R6.9).
///
/// <para>
/// <b>Por que um helper dedicado.</b> A recusa de acesso cross-tenant é produzida
/// em dois pontos: (1) escritas divergentes disparam
/// <c>CrossTenantAccessException</c> no interceptor de <c>SaveChanges</c> do
/// <c>AppDbContext</c>; (2) leituras de outro tenant retornam 404 nos serviços de
/// negócio. Auditar diretamente de dentro do <c>AppDbContext</c> exigiria injetar
/// o <see cref="IAuditLogger"/> nele — que, por sua vez, depende de uma fábrica de
/// <c>AppDbContext</c> —, criando um ciclo de dependências. Este helper quebra o
/// ciclo: ele depende apenas do <see cref="IAuditLogger"/> (que grava por um
/// contexto isolado) e é invocado <b>na fronteira</b> que captura a recusa — o
/// middleware global de erros (tarefa 9.1) e os serviços de negócio (tarefas 6–8),
/// à medida que forem introduzidos.
/// </para>
///
/// <para>
/// O evento é registrado com <see cref="AuditResult.Denied"/> e a ação
/// <see cref="CrossTenantDeniedAction"/>. Como a recusa pode ocorrer sem um tenant
/// coerente no contexto, <paramref name="tenantId"/> é anulável.
/// </para>
/// </summary>
public interface ICrossTenantAuditor
{
    /// <summary>Ação canônica registrada para recusas de acesso cross-tenant (R6.9).</summary>
    public const string CrossTenantDeniedAction = "security.cross_tenant_denied";

    /// <summary>
    /// Registra uma recusa de acesso cross-tenant (R6.9) com
    /// <see cref="AuditResult.Denied"/>.
    /// </summary>
    /// <param name="actorUserId">Ator que originou a tentativa, quando conhecido.</param>
    /// <param name="tenantId">Tenant do contexto autenticado, quando resolvido.</param>
    /// <param name="resourceType">Tipo do recurso alvo da tentativa recusada.</param>
    /// <param name="resourceId">Identificador do recurso alvo, quando conhecido.</param>
    /// <param name="ip">Endereço IP de origem, quando disponível.</param>
    /// <param name="userAgent">User-Agent de origem, quando disponível.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task RecordDeniedAsync(
        Guid? actorUserId,
        Guid? tenantId,
        string resourceType,
        string? resourceId,
        string? ip,
        string? userAgent,
        CancellationToken ct);
}
