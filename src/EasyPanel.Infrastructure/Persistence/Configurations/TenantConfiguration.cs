using EasyPanel.Modules.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Tenant"/> — a raiz de
/// isolamento multi-tenant (R6.1).
///
/// Reside no assembly de Infrastructure para ser descoberta por
/// <c>ApplyConfigurationsFromAssembly</c> no <see cref="AppDbContext"/>.
/// </summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.IsActive)
            .IsRequired();

        // Slug é o identificador legível público do tenant: único globalmente.
        builder.HasIndex(t => t.Slug)
            .IsUnique();
    }
}
