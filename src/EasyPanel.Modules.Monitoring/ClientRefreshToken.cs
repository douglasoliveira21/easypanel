using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Refresh token opaco e rotativo de um <see cref="WindowsClient"/> (R4.4),
/// análogo ao refresh token de usuário: o valor bruto é entregue ao agente, mas
/// apenas o hash (SHA-256) é persistido. A cada renovação o token é rotacionado
/// (revogado + sucessor emitido). É uma <see cref="TenantEntity"/> — isolado por
/// tenant como o restante dos dados do agente.
/// </summary>
public class ClientRefreshToken : TenantEntity
{
    /// <summary>Agente proprietário do token (R4.4).</summary>
    public Guid WindowsClientId { get; set; }

    /// <summary>Hash SHA-256 (base64url) do valor bruto do token. Nunca em texto claro.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Expiração do token (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Momento de revogação (UTC), quando rotacionado/invalidado.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Hash do token sucessor, quando rotacionado (encadeamento).</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Indica se o token está ativo (não revogado e não expirado) no instante dado.</summary>
    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
