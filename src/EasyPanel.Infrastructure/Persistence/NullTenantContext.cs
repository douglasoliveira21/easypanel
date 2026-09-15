using EasyPanel.Modules.Tenancy;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Implementação inerte de <see cref="ITenantContext"/> sem tenant e sem
/// privilégios de Super Admin.
///
/// Usada em cenários fora do pipeline de requisição — notadamente pela fábrica
/// de design-time <see cref="AppDbContextFactory"/>, onde as ferramentas do EF
/// Core (dotnet ef) materializam o modelo para gerar migrações sem um
/// <see cref="ITenantContext"/> resolvido. O filtro global e o interceptor de
/// escrita não alteram o schema, portanto este contexto nulo é suficiente para
/// o scaffolding.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    /// <summary>Instância compartilhada e imutável.</summary>
    public static readonly NullTenantContext Instance = new();

    /// <inheritdoc />
    public Guid? TenantId => null;

    /// <inheritdoc />
    public bool IsSuperAdmin => false;

    /// <inheritdoc />
    public bool HasTenant => false;
}
