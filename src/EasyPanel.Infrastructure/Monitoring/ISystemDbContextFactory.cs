using EasyPanel.Infrastructure.Persistence;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Fábrica de <see cref="AppDbContext"/> efêmero com contexto de tenant de sistema
/// (Super Admin), para operações confiáveis fora do pipeline autenticado (ex.:
/// registro de agentes — R3). Distinta de <c>IDbContextFactory&lt;AppDbContext&gt;</c>
/// (usada pela auditoria) para não conflitar no contêiner de DI.
/// </summary>
public interface ISystemDbContextFactory
{
    /// <summary>Cria um <see cref="AppDbContext"/> com <see cref="SystemTenantContext"/>.</summary>
    AppDbContext Create();
}

/// <summary>
/// Implementação de <see cref="ISystemDbContextFactory"/> que reutiliza as
/// <c>DbContextOptions</c> compartilhadas e injeta o <see cref="SystemTenantContext"/>.
/// </summary>
public sealed class SystemDbContextFactory : ISystemDbContextFactory
{
    private readonly Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext> _options;

    public SystemDbContextFactory(Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public AppDbContext Create() => new(_options, SystemTenantContext.Instance);
}
