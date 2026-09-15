using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Unidade de isolamento de dados de negócio da plataforma (R6.1).
///
/// O <see cref="Tenant"/> é a raiz do isolamento multi-tenant e, por isso,
/// deriva de <see cref="BaseEntity"/> — e não de <c>TenantEntity</c>: ele
/// próprio não pertence a outro tenant. <see cref="BaseEntity.Id"/> e
/// <see cref="BaseEntity.CreatedAt"/> são herdados.
/// </summary>
public class Tenant : BaseEntity
{
    /// <summary>Nome de exibição do tenant.</summary>
    public required string Name { get; set; }

    /// <summary>Identificador legível e único usado em URLs/rotas (ex.: "acme").</summary>
    public required string Slug { get; set; }

    /// <summary>Indica se o tenant está ativo na plataforma.</summary>
    public bool IsActive { get; set; }
}
