using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="SupplyReading"/> (Fase 4).
/// Somente-adição; cursor de histórico por <c>TimestampTicks</c> (portável).
/// </summary>
internal sealed class SupplyReadingConfiguration : IEntityTypeConfiguration<SupplyReading>
{
    public void Configure(EntityTypeBuilder<SupplyReading> builder)
    {
        builder.ToTable("SupplyReadings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Label).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Percent).IsRequired();
        builder.Property(s => s.Timestamp).IsRequired();
        builder.Property(s => s.TimestampTicks).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(s => s.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Histórico por impressora/rótulo, cursor pagination por TimestampTicks.
        builder.HasIndex(s => new { s.TenantId, s.PrinterId, s.Label, s.TimestampTicks });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(s => s.TenantId);
    }
}
