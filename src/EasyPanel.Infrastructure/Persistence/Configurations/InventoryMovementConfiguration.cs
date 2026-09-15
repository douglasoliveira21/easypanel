using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="InventoryMovement"/> (Fase 5 — R2).
/// Somente-adição; cursor de histórico por <c>OccurredAtTicks</c> (portável).
/// </summary>
internal sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("InventoryMovements");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).IsRequired();
        builder.Property(m => m.Quantity).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.OccurredAtTicks).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasOne<InventoryItem>()
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(m => m.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(m => m.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Histórico por (item, local), cursor pagination por OccurredAtTicks (R3.3).
        builder.HasIndex(m => new { m.TenantId, m.ItemId, m.LocationId, m.OccurredAtTicks });

        // Histórico por impressora (R4.3).
        builder.HasIndex(m => new { m.TenantId, m.PrinterId, m.OccurredAtTicks });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(m => m.TenantId);
    }
}
