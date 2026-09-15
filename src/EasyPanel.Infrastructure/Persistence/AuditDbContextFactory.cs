using EasyPanel.Modules.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Fábrica de runtime de <see cref="AppDbContext"/> dedicada à trilha de auditoria
/// (R10). Cria contextos efêmeros e independentes do <c>AppDbContext</c> scoped da
/// requisição, de modo que a gravação de um <c>AuditLog</c> não faça flush de
/// alterações pendentes de outras entidades (ver <c>AuditLogger</c>).
///
/// <para>
/// Reutiliza as <see cref="DbContextOptions{TContext}"/> já configuradas por
/// <c>AddPersistence</c> (mesma cadeia de conexão/provider) e injeta um
/// <see cref="ITenantContext"/> inerte (<see cref="NullTenantContext"/>): como
/// <c>AuditLog</c> não é uma <c>TenantEntity</c>, o filtro global e o interceptor de
/// tenant não se aplicam, e nenhum contexto de tenant é necessário para inserir o
/// registro.
/// </para>
///
/// <para>
/// Implementar uma fábrica própria (em vez de <c>AddDbContextFactory</c>) evita o
/// conflito de registro do <see cref="DbContextOptions{TContext}"/> singleton com o
/// <c>AppDbContext</c> scoped já registrado por <c>AddDbContext</c>.
/// </para>
/// </summary>
public sealed class AuditDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    /// <summary>Cria a fábrica com as opções compartilhadas do <see cref="AppDbContext"/>.</summary>
    public AuditDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public AppDbContext CreateDbContext()
        => new(_options, NullTenantContext.Instance);
}
