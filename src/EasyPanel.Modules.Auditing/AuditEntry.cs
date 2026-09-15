namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Descreve um evento auditável a ser registrado via <see cref="IAuditLogger"/>.
///
/// É o contrato de entrada da auditoria: o chamador informa o que aconteceu, e o
/// logger cuida de materializar um <see cref="AuditLog"/> imutável (atribuindo
/// <c>Id</c> e <c>OccurredAt</c>). O <c>OccurredAt</c> não faz parte deste contrato
/// porque é sempre carimbado pelo logger no momento da gravação.
/// </summary>
public sealed record AuditEntry
{
    /// <summary>Ator que originou o evento, quando conhecido.</summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>
    /// Tenant no qual a ação ocorreu (R10.3). Anulável para eventos de plataforma
    /// sem tenant resolvido (ex.: falha de login anônima).
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>Ação executada, no formato <c>recurso.acao</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Tipo do recurso afetado.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Identificador do recurso afetado, quando aplicável.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Valores anteriores (JSON, com redaction), quando aplicável.</summary>
    public string? OldValues { get; init; }

    /// <summary>Valores novos (JSON, com redaction), quando aplicável.</summary>
    public string? NewValues { get; init; }

    /// <summary>Endereço IP de origem, quando disponível.</summary>
    public string? Ip { get; init; }

    /// <summary>User-Agent de origem, quando disponível.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Resultado do evento.</summary>
    public AuditResult Result { get; init; }
}
