using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="AlertRule"/> (R1, R17.7-equivalente
/// da Fase 3). <see cref="AlertRule.WebhookSecret"/> nunca é lido fora da
/// Infrastructure (DTOs nunca o expõem — R5.6).
/// </summary>
internal sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("AlertRules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.EventTypesCsv).IsRequired().HasMaxLength(200);
        builder.Property(r => r.ScopeType).IsRequired();
        builder.Property(r => r.Severity).IsRequired();
        builder.Property(r => r.AutoResolve).IsRequired();
        builder.Property(r => r.EmailEnabled).IsRequired();
        builder.Property(r => r.EmailRecipientsCsv).HasMaxLength(2000);
        builder.Property(r => r.WebhookEnabled).IsRequired();
        builder.Property(r => r.WebhookUrl).HasMaxLength(500);
        builder.Property(r => r.WebhookSecret).HasMaxLength(200);
        builder.Property(r => r.CreatedAt).IsRequired();

        // Escopo opcional: apenas um FK é relevante por regra, conforme ScopeType.
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(r => r.ScopeLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(r => r.ScopePrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WindowsClient>()
            .WithMany()
            .HasForeignKey(r => r.ScopeWindowsClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem por tenant com filtro de ativo/escopo (R1.6).
        builder.HasIndex(r => new { r.TenantId, r.IsActive });
        builder.HasIndex(r => new { r.TenantId, r.ScopeType });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(r => r.TenantId);
    }
}
