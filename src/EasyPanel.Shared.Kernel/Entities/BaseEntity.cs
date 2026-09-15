namespace EasyPanel.Shared.Kernel.Entities;

/// <summary>
/// Entidade base para todas as entidades persistidas da plataforma.
/// Fornece identidade e carimbos temporais de auditoria em UTC.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>Identificador único da entidade.</summary>
    public Guid Id { get; set; }

    /// <summary>Momento de criação (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Momento da última atualização (UTC), quando houver.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
