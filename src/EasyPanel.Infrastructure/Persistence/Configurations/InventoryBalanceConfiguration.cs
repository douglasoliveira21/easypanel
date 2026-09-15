using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="InventoryBalance"/> (Fase 5 — R3.1).
/// Índice único por (Item, Local) dentro do tenant — no máximo uma linha de saldo
/// por combinação.
/// </summary>
internal sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("InventoryBalances");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Quantity).IsRequired();
        builder.Property(b => b.CreatedAt).IsRequired();

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(b => b.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(b => b.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.TenantId, b.ItemId, b.LocationId }).IsUnique();

        // Consulta de saldo por Local (R3.2).
        builder.HasIndex(b => new { b.TenantId, b.LocationId });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(b => b.TenantId);
    }
}
