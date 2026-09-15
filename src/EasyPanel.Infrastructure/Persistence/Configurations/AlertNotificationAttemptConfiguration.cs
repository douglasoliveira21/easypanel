using EasyPanel.Modules.Alerting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="AlertNotificationAttempt"/> (R6).
/// Somente-adição; histórico de diagnóstico de entrega por cursor pagination.
/// </summary>
internal sealed class AlertNotificationAttemptConfiguration : IEntityTypeConfiguration<AlertNotificationAttempt>
{
    public void Configure(EntityTypeBuilder<AlertNotificationAttempt> builder)
    {
        builder.ToTable("AlertNotificationAttempts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Channel).IsRequired();
        builder.Property(a => a.Outcome).IsRequired();
        builder.Property(a => a.AttemptNumber).IsRequired();
        builder.Property(a => a.ErrorSummary).HasMaxLength(1000);
        builder.Property(a => a.AttemptedAt).IsRequired();
        builder.Property(a => a.AttemptedAtTicks).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<Alert>()
            .WithMany()
            .HasForeignKey(a => a.AlertId)
            .OnDelete(DeleteBehavior.Restrict);

        // Histórico por Alerta, cursor pagination por AttemptedAtTicks (R6.2).
        builder.HasIndex(a => new { a.TenantId, a.AlertId, a.AttemptedAtTicks });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(a => a.TenantId);
    }
}
