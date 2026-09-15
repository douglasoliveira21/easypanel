using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="IngestionDedup"/> (R13.1, R13.3).
///
/// Índices:
/// <list type="bullet">
///   <item>
///     <c>UNIQUE (TenantId, IdempotencyKey)</c>: backstop de idempotência de
///     ingestão além do índice único em <see cref="Collection"/> (R13.1/R13.3).
///   </item>
/// </list>
/// </summary>
internal sealed class IngestionDedupConfiguration : IEntityTypeConfiguration<IngestionDedup>
{
    public void Configure(EntityTypeBuilder<IngestionDedup> builder)
    {
        builder.ToTable("IngestionDedups");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(d => d.CollectionId)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        // Backstop de idempotência por tenant (R13.1/R13.3).
        builder.HasIndex(d => new { d.TenantId, d.IdempotencyKey })
            .IsUnique();

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(d => d.TenantId);
    }
}
