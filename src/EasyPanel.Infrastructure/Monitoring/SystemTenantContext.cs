using EasyPanel.Modules.Tenancy;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Contexto de tenant "de sistema" (Super Admin) usado por operações confiáveis do
/// servidor que ocorrem fora de um contexto de requisição autenticado — notadamente
/// o registro de agentes (R3), que é anônimo mas precisa gravar uma
/// <c>TenantEntity</c> com o <c>TenantId</c> resolvido da chave de provisionamento.
///
/// <para>Ao se comportar como Super Admin, o interceptor de escrita do
/// <c>AppDbContext</c> permite persistir a entidade com o <c>TenantId</c> explícito
/// que a operação já validou, sem violar o isolamento (o tenant vem sempre da chave
/// de provisionamento, jamais da requisição).</para>
/// </summary>
public sealed class SystemTenantContext : ITenantContext
{
    /// <summary>Instância compartilhada e imutável.</summary>
    public static readonly SystemTenantContext Instance = new();

    /// <inheritdoc />
    public Guid? TenantId => null;

    /// <inheritdoc />
    public bool IsSuperAdmin => true;

    /// <inheritdoc />
    public bool HasTenant => false;
}
