using EasyPanel.Modules.Alerting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="AlertNotificationOutbox"/> (R4/R5).
/// Drenada pelo <c>AlertNotificationDispatcher</c> por <c>(Status, NextAttemptAt)</c>.
/// </summary>
internal sealed class AlertNotificationOutboxConfiguration : IEntityTypeConfiguration<AlertNotificationOutbox>
{
    public void Configure(EntityTypeBuilder<AlertNotificationOutbox> builder)
    {
        builder.ToTable("AlertNotificationOutbox");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Channel).IsRequired();
        builder.Property(o => o.Status).IsRequired();
        builder.Property(o => o.AttemptCount).IsRequired();
        builder.Property(o => o.NextAttemptAt).IsRequired();
        builder.Property(o => o.CreatedAt).IsRequired();

        builder.HasOne<Alert>()
            .WithMany()
            .HasForeignKey(o => o.AlertId)
            .OnDelete(DeleteBehavior.Restrict);

        // Fila de despacho do AlertNotificationDispatcher.
        builder.HasIndex(o => new { o.Status, o.NextAttemptAt });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(o => o.TenantId);
    }
}
