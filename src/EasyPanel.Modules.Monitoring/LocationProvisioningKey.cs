using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Credencial de provisionamento vinculada a um Local (R3.2), usada uma única vez
/// (ou por tempo limitado) para registrar agentes naquele Local. O valor bruto é
/// entregue ao operador que instala o agente; apenas o hash é persistido. É uma
/// <see cref="TenantEntity"/> — isolada por tenant como o restante dos dados.
///
/// <para>Múltiplos agentes podem ser registrados com a mesma chave enquanto ela
/// estiver ativa (R3.6 permite múltiplos clients por Cliente/Local); a expiração e
/// a revogação controlam a janela de provisionamento.</para>
/// </summary>
public class LocationProvisioningKey : TenantEntity
{
    /// <summary>Cliente ao qual o Local pertence (R3.3).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Local que a chave provisiona (R3.2).</summary>
    public Guid LocationId { get; set; }

    /// <summary>Hash SHA-256 (base64url) do valor bruto da chave. Nunca em texto claro.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Expiração da chave (UTC), quando aplicável.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Momento de revogação (UTC), quando desativada manualmente.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Descrição livre para operação (ex.: "Matriz - piso 3").</summary>
    public string? Description { get; set; }

    /// <summary>Indica se a chave está ativa (não revogada e não expirada) no instante dado.</summary>
    public bool IsActiveAt(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
