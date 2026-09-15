using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="SupplyThreshold"/> (Fase 4). No
/// máximo uma linha por <c>(TenantId, PrinterId, Label)</c> — inclusive quando
/// <c>PrinterId</c>/<c>Label</c> são nulos (padrões de cascata), garantido em
/// código pelo <c>ISupplyService</c> (upsert), não por constraint de banco: NULL
/// não é igual a NULL num índice único do Postgres/SQLite.
/// </summary>
internal sealed class SupplyThresholdConfiguration : IEntityTypeConfiguration<SupplyThreshold>
{
    public void Configure(EntityTypeBuilder<SupplyThreshold> builder)
    {
        builder.ToTable("SupplyThresholds");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Label).HasMaxLength(100);
        builder.Property(s => s.ThresholdPercent).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(s => s.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Resolução em cascata (Impressora+Rótulo -> Tenant+Rótulo -> Tenant geral).
        builder.HasIndex(s => new { s.TenantId, s.PrinterId, s.Label });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(s => s.TenantId);
    }
}
