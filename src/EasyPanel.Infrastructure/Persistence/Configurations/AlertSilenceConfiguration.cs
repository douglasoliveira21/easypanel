using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>Configuração EF Core da entidade <see cref="AlertSilence"/> (R7).</summary>
internal sealed class AlertSilenceConfiguration : IEntityTypeConfiguration<AlertSilence>
{
    public void Configure(EntityTypeBuilder<AlertSilence> builder)
    {
        builder.ToTable("AlertSilences");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.StartsAt).IsRequired();
        builder.Property(s => s.EndsAt).IsRequired();
        builder.Property(s => s.CreatedByUserId).IsRequired();
        builder.Property(s => s.Reason).HasMaxLength(1000);
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasOne<AlertRule>()
            .WithMany()
            .HasForeignKey(s => s.AlertRuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(s => s.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WindowsClient>()
            .WithMany()
            .HasForeignKey(s => s.WindowsClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Consulta de vigência por escopo (verificada pelo AlertNotificationDispatcher — R7.3).
        builder.HasIndex(s => new { s.TenantId, s.AlertRuleId });
        builder.HasIndex(s => new { s.TenantId, s.PrinterId });
        builder.HasIndex(s => new { s.TenantId, s.WindowsClientId });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(s => s.TenantId);
    }
}
