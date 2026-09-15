namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Implementação <c>scoped</c> e mutável de <see cref="ITenantContext"/>.
///
/// O tenant resolvido é preenchido pela camada de resolução (middleware),
/// implementada em tarefa posterior, por meio de <see cref="SetTenant"/> ou
/// <see cref="SetSuperAdmin"/>. Enquanto não resolvido, o contexto permanece
/// sem tenant (<see cref="HasTenant"/> retorna <c>false</c>).
/// </summary>
public sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    public bool IsSuperAdmin { get; private set; }

    /// <inheritdoc />
    public bool HasTenant => TenantId.HasValue;

    /// <summary>
    /// Define o tenant resolvido para a requisição corrente.
    /// </summary>
    /// <param name="tenantId">Identificador do tenant autenticado.</param>
    public void SetTenant(Guid tenantId)
    {
        TenantId = tenantId;
        IsSuperAdmin = false;
    }

    /// <summary>
    /// Marca o contexto como Super Admin da plataforma, opcionalmente com um
    /// tenant alvo resolvido a partir de um endpoint administrativo designado
    /// (R6.7).
    /// </summary>
    /// <param name="tenantId">
    /// Tenant alvo da operação administrativa, quando aplicável; <c>null</c>
    /// para operações fora de um tenant específico.
    /// </param>
    public void SetSuperAdmin(Guid? tenantId = null)
    {
        TenantId = tenantId;
        IsSuperAdmin = true;
    }
}
