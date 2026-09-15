using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="LocationProvisioningKey"/> (R3.2).
///
/// Índices:
/// <list type="bullet">
///   <item><c>UNIQUE (KeyHash)</c>: localização da chave por hash na validação de registro.</item>
///   <item><c>(TenantId, LocationId)</c>: chaves por local.</item>
/// </list>
/// </summary>
internal sealed class LocationProvisioningKeyConfiguration
    : IEntityTypeConfiguration<LocationProvisioningKey>
{
    public void Configure(EntityTypeBuilder<LocationProvisioningKey> builder)
    {
        builder.ToTable("LocationProvisioningKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.KeyHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(k => k.Description).HasMaxLength(256);
        builder.Property(k => k.CreatedAt).IsRequired();

        builder.HasOne<EasyPanel.Modules.Customers.Location>()
            .WithMany()
            .HasForeignKey(k => k.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Localização por hash na validação de registro (R3.2).
        builder.HasIndex(k => k.KeyHash).IsUnique();

        // Chaves por local dentro do tenant.
        builder.HasIndex(k => new { k.TenantId, k.LocationId });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(k => k.TenantId);
    }
}
