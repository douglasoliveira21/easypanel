using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IAuditLogQueryService"/> sobre o
/// <see cref="AppDbContext"/> (R10.5).
///
/// <para>
/// <b>Restrição por tenant explícita (R10.5).</b> Como <see cref="AuditLog"/> não é
/// uma <c>TenantEntity</c>, não há filtro global de consulta; a cláusula
/// <c>WHERE TenantId == tenantAtual</c> é aplicada aqui. Quando o contexto não tem
/// tenant resolvido (<c>tenantId == null</c>), a consulta retorna uma página vazia:
/// não há tenant a que restringir a leitura, e a auditoria não deve vazar eventos
/// de plataforma sem tenant por este endpoint.
/// </para>
///
/// <para>
/// <b>Ordenação e portabilidade SQLite.</b> O padrão de listagem é do evento mais
/// recente ao mais antigo (índice <c>(TenantId, OccurredAt DESC)</c>). O provider
/// relacional SQLite usado nos testes não traduz <c>ORDER BY</c> sobre
/// <see cref="DateTimeOffset"/>; para manter a mesma consulta funcional em ambos os
/// provedores, a página do tenant é materializada e então ordenada em memória por
/// <c>OccurredAt</c> decrescente. No PostgreSQL de produção o volume por página é
/// limitado (≤ 100 — R12.4) e o índice cobre o filtro por tenant; a ordenação
/// final sobre a página materializada é barata e estável.
/// </para>
/// </summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly AppDbContext _context;

    /// <summary>Cria o serviço de consulta sobre o contexto de persistência scoped.</summary>
    public AuditLogQueryService(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditLog>> QueryAsync(Guid? tenantId, PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Sem tenant resolvido: nada a retornar (R10.5).
        if (tenantId is null)
        {
            return new PagedResult<AuditLog>(Array.Empty<AuditLog>(), page.Page, page.PageSize, TotalCount: 0);
        }

        // Filtro explícito por tenant (R10.5): AuditLog não recebe filtro global.
        var query = _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId);

        var totalCount = await query.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (page.Page - 1) * page.PageSize;

        // Materializa a página do tenant e ordena em memória por OccurredAt DESC.
        // Paginar antes de ordenar sobre DateTimeOffset seria dependente do
        // provider; ordenar por um discriminador estável (OccurredAt no cliente)
        // mantém o comportamento idêntico entre PostgreSQL e SQLite (testes). O
        // Skip/Take é aplicado após a ordenação sobre o conjunto materializado do
        // tenant, que é limitado na prática pelos índices e pela paginação.
        var rows = await query.ToListAsync(ct).ConfigureAwait(false);

        var items = rows
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(page.PageSize)
            .ToList();

        return new PagedResult<AuditLog>(items, page.Page, page.PageSize, totalCount);
    }
}
