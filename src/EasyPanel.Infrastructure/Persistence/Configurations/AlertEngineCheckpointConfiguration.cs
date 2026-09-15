using EasyPanel.Modules.Alerting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core do cursor único de plataforma <see cref="AlertEngineCheckpoint"/>
/// (R2.1/R2.8). Não é uma <c>TenantEntity</c>: não recebe filtro global de tenant,
/// no mesmo padrão de <c>AuditLog</c>.
/// </summary>
internal sealed class AlertEngineCheckpointConfiguration : IEntityTypeConfiguration<AlertEngineCheckpoint>
{
    public void Configure(EntityTypeBuilder<AlertEngineCheckpoint> builder)
    {
        builder.ToTable("AlertEngineCheckpoints");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.LastProcessedEventTicks).IsRequired();
        builder.Property(c => c.LastProcessedEventId).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
    }
}
