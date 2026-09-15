using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="PrinterMovement"/> (R10, R17.7).
/// Histórico somente-adição de movimentação/ciclo de vida.
///
/// Índices (design "Índices principais" da Fase 2):
/// <list type="bullet">
///   <item><c>(TenantId, PrinterId, OccurredAt)</c>: histórico por impressora (R10.2).</item>
/// </list>
/// </summary>
internal sealed class PrinterMovementConfiguration : IEntityTypeConfiguration<PrinterMovement>
{
    public void Configure(EntityTypeBuilder<PrinterMovement> builder)
    {
        builder.ToTable("PrinterMovements");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Operation).IsRequired();
        builder.Property(m => m.OccurredAt).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        // Relacionamento 1:N Printer → PrinterMovements (R10.2).
        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(m => m.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Histórico por impressora dentro do tenant (R10.2).
        builder.HasIndex(m => new { m.TenantId, m.PrinterId, m.OccurredAt });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(m => m.TenantId);
    }
}
