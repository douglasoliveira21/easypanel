using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="ClientRefreshToken"/> (R4.4).
///
/// Índices:
/// <list type="bullet">
///   <item><c>UNIQUE (TokenHash)</c>: localização do token por hash na validação.</item>
///   <item><c>(TenantId, WindowsClientId)</c>: revogação/consulta por agente.</item>
/// </list>
/// </summary>
internal sealed class ClientRefreshTokenConfiguration : IEntityTypeConfiguration<ClientRefreshToken>
{
    public void Configure(EntityTypeBuilder<ClientRefreshToken> builder)
    {
        builder.ToTable("ClientRefreshTokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(256);
        builder.Property(t => t.CreatedAt).IsRequired();

        // Relacionamento 1:N WindowsClient → ClientRefreshTokens (R4.4).
        builder.HasOne<WindowsClient>()
            .WithMany()
            .HasForeignKey(t => t.WindowsClientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Localização por hash na validação/rotação.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Revogação/consulta por agente dentro do tenant.
        builder.HasIndex(t => new { t.TenantId, t.WindowsClientId });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(t => t.TenantId);
    }
}
