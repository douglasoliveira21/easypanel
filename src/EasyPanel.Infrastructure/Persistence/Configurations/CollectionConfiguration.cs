using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Collection"/> (R12, R13.1, R17.7).
///
/// Índices (design "Índices principais" da Fase 2):
/// <list type="bullet">
///   <item><c>UNIQUE (TenantId, IdempotencyKey)</c>: idempotência de ingestão (R13.1).</item>
///   <item><c>(TenantId, WindowsClientId, StartedAt)</c>: histórico por agente.</item>
///   <item><c>(TenantId, PrinterId, StartedAt)</c>: histórico por impressora.</item>
/// </list>
/// </summary>
internal sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.StartedAt).IsRequired();
        builder.Property(c => c.Result).IsRequired();
        builder.Property(c => c.AttemptCount).IsRequired();

        // JSON/erros de tamanho variável como text (portável PostgreSQL/SQLite).
        builder.Property(c => c.Errors).HasColumnType("text");
        builder.Property(c => c.CollectedData).HasColumnType("text");

        builder.Property(c => c.CreatedAt).IsRequired();

        // Relacionamento 1:N WindowsClient → Collections (R12.1).
        builder.HasOne<WindowsClient>()
            .WithMany()
            .HasForeignKey(c => c.WindowsClientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotência de ingestão (R13.1): reenvio não duplica dados.
        builder.HasIndex(c => new { c.TenantId, c.IdempotencyKey })
            .IsUnique();

        // Histórico por agente/impressora dentro do tenant (R12).
        builder.HasIndex(c => new { c.TenantId, c.WindowsClientId, c.StartedAt });
        builder.HasIndex(c => new { c.TenantId, c.PrinterId, c.StartedAt });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(c => c.TenantId);
    }
}
