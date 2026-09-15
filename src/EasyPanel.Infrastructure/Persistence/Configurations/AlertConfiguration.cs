using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Alert"/> (R3, R2.5).
///
/// Índices:
/// <list type="bullet">
///   <item><c>(TenantId, State, LastOccurrenceAtTicks)</c>: listagem/cursor (R3.6).</item>
///   <item><c>(TenantId, AlertRuleId, PrinterId, WindowsClientId, State)</c>: dedupe
///     de Alerta aberto/reconhecido por alvo (R2.5), consultado pelo AlertEngine.</item>
/// </list>
/// </summary>
internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Severity).IsRequired();
        builder.Property(a => a.State).IsRequired();
        builder.Property(a => a.FirstOccurrenceAt).IsRequired();
        builder.Property(a => a.FirstOccurrenceAtTicks).IsRequired();
        builder.Property(a => a.LastOccurrenceAt).IsRequired();
        builder.Property(a => a.LastOccurrenceAtTicks).IsRequired();
        builder.Property(a => a.OccurrenceCount).IsRequired();
        builder.Property(a => a.AutoResolved).IsRequired();
        builder.Property(a => a.ResolutionNote).HasMaxLength(2000);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<AlertRule>()
            .WithMany()
            .HasForeignKey(a => a.AlertRuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(a => a.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WindowsClient>()
            .WithMany()
            .HasForeignKey(a => a.WindowsClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.TenantId, a.State, a.LastOccurrenceAtTicks });
        builder.HasIndex(a => new { a.TenantId, a.AlertRuleId, a.PrinterId, a.WindowsClientId, a.State });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(a => a.TenantId);
    }
}
