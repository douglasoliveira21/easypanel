using EasyPanel.Modules.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>Configuração EF Core da entidade <see cref="InventoryItem"/> (Fase 5 — R1).</summary>
internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Sku).HasMaxLength(100);
        builder.Property(i => i.Unit).IsRequired().HasMaxLength(20);
        builder.Property(i => i.SupplyLabel).HasMaxLength(100);
        builder.Property(i => i.IsActive).IsRequired();
        builder.Property(i => i.Observations).HasMaxLength(1000);
        builder.Property(i => i.CreatedAt).IsRequired();

        // Busca por nome/SKU dentro do tenant (R1.4).
        builder.HasIndex(i => new { i.TenantId, i.Name });
        builder.HasIndex(i => new { i.TenantId, i.Sku });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(i => i.TenantId);
    }
}
