using EasyPanel.Shared.Kernel.Pagination;

namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Consulta paginada da trilha de auditoria, restrita ao tenant do contexto
/// autenticado (R10.5).
///
/// <para>
/// Diferente das entidades de negócio, <see cref="AuditLog"/> não é uma
/// <c>TenantEntity</c> e, portanto, não recebe o filtro global de tenant do ORM. A
/// restrição por tenant é aplicada <b>explicitamente</b> por esta consulta
/// (<c>WHERE TenantId == tenantAtual</c>), garantindo que um administrador só
/// enxergue os eventos do próprio tenant (R10.5). Os resultados são ordenados do
/// evento mais recente para o mais antigo.
/// </para>
/// </summary>
public interface IAuditLogQueryService
{
    /// <summary>
    /// Retorna uma página de eventos de auditoria do tenant informado, ordenada por
    /// <c>OccurredAt</c> decrescente (R10.5). Quando <paramref name="tenantId"/> é
    /// <c>null</c>, retorna uma página vazia — não há tenant a que restringir a
    /// leitura.
    /// </summary>
    /// <param name="tenantId">Tenant do contexto autenticado, ou <c>null</c>.</param>
    /// <param name="page">Parâmetros de paginação (PageSize já limitado — R12.4).</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<PagedResult<AuditLog>> QueryAsync(Guid? tenantId, PageRequest page, CancellationToken ct);
}
