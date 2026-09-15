using EasyPanel.Modules.Ticketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="SlaPolicy"/> (Fase 6 — R4.1).
/// Índice único por Prioridade dentro do tenant — upsert.
/// </summary>
internal sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("SlaPolicies");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Priority).IsRequired();
        builder.Property(p => p.FirstResponseMinutes).IsRequired();
        builder.Property(p => p.ResolutionMinutes).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.Priority }).IsUnique();
    }
}
