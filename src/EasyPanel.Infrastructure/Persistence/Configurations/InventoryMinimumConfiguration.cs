using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="InventoryMinimum"/> (Fase 5 — R5.1).
/// Índice único por (Item, Local) dentro do tenant.
/// </summary>
internal sealed class InventoryMinimumConfiguration : IEntityTypeConfiguration<InventoryMinimum>
{
    public void Configure(EntityTypeBuilder<InventoryMinimum> builder)
    {
        builder.ToTable("InventoryMinimums");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.MinimumQuantity).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(m => m.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.TenantId, m.ItemId, m.LocationId }).IsUnique();

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(m => m.TenantId);
    }
}
