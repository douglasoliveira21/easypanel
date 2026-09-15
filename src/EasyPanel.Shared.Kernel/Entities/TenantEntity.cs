namespace EasyPanel.Shared.Kernel.Entities;

/// <summary>
/// Entidade base para todo dado de negócio isolado por tenant.
/// O <see cref="TenantId"/> é a unidade de isolamento multi-tenant (R6).
/// </summary>
public abstract class TenantEntity : BaseEntity
{
    /// <summary>Identificador do tenant proprietário do registro.</summary>
    public Guid TenantId { get; set; }
}
