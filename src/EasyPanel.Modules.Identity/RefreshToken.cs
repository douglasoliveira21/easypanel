using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Refresh token opaco e rotativo de um <see cref="ApplicationUser"/> (R2.1, R2.4, R2.5).
///
/// O valor bruto (opaco) entregue ao cliente <b>nunca</b> é persistido: o banco
/// armazena apenas o seu hash SHA-256 em <see cref="TokenHash"/>. Isso garante
/// que um vazamento da tabela não exponha tokens utilizáveis.
///
/// A cada uso bem-sucedido o token é <i>rotacionado</i>: o registro corrente é
/// revogado (<see cref="RevokedAt"/> preenchido, <see cref="ReplacedByTokenHash"/>
/// apontando para o sucessor) e um novo registro é emitido. Um token revogado,
/// expirado ou desconhecido nunca volta a ser válido (invariante de
/// monotonicidade de revogação — R2.5).
///
/// Deriva de <see cref="BaseEntity"/> (Id/CreatedAt gerados pela aplicação, PK
/// Guid). Não é <see cref="TenantEntity"/>: o refresh token pertence a um usuário,
/// e o usuário carrega o tenant; portanto não recebe filtro global de tenant.
/// </summary>
public class RefreshToken : BaseEntity
{
    /// <summary>Usuário dono do token.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Hash SHA-256 (base64url) do valor bruto do token. É o único vestígio do
    /// token guardado no banco; o valor bruto não é persistido.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Momento de expiração do token (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Momento de revogação (UTC), quando revogado; nulo enquanto ativo.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Hash do token que substituiu este por rotação, quando aplicável. Permite
    /// reconstruir a cadeia de rotação para fins de auditoria/detecção de reuso.
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>
    /// Indica se o token está ativo no instante <paramref name="now"/>: não
    /// revogado e não expirado.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
