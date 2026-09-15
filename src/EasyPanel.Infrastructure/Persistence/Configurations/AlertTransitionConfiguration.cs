using EasyPanel.Modules.Alerting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="AlertTransition"/> (R3.5).
/// Somente-adição: nenhuma operação de update/delete é exposta pelos serviços.
/// </summary>
internal sealed class AlertTransitionConfiguration : IEntityTypeConfiguration<AlertTransition>
{
    public void Configure(EntityTypeBuilder<AlertTransition> builder)
    {
        builder.ToTable("AlertTransitions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.FromState).IsRequired();
        builder.Property(t => t.ToState).IsRequired();
        builder.Property(t => t.OccurredAt).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(2000);
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasOne<Alert>()
            .WithMany()
            .HasForeignKey(t => t.AlertId)
            .OnDelete(DeleteBehavior.Restrict);

        // Histórico por Alerta, cronológico.
        builder.HasIndex(t => new { t.TenantId, t.AlertId, t.OccurredAt });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(t => t.TenantId);
    }
}
