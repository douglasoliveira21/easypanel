using EasyPanel.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core de <see cref="RefreshToken"/> (R2.1, R2.4, R2.5).
///
/// Índices (design "Índices principais"):
/// <list type="bullet">
///   <item><c>UNIQUE (TokenHash)</c>: um hash identifica no máximo um token e
///   acelera a validação por hash.</item>
///   <item><c>(UserId)</c>: revogação/listagem por usuário.</item>
///   <item><c>(ExpiresAt)</c>: limpeza de tokens expirados.</item>
/// </list>
///
/// O <c>TokenHash</c> é obrigatório e tem comprimento fixo previsível (hash
/// SHA-256 em base64url, 43 caracteres); reservamos 64 por folga. O
/// <c>ReplacedByTokenHash</c> segue o mesmo tamanho, porém é anulável.
/// </summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    private const int HashLength = 64;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(HashLength);

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.Property(t => t.ReplacedByTokenHash)
            .HasMaxLength(HashLength);

        builder.HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.HasIndex(t => t.UserId);

        builder.HasIndex(t => t.ExpiresAt);

        // FK lógica para o usuário dono; sem propriedade de navegação para manter
        // o módulo de Identity desacoplado do grafo de persistência.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
