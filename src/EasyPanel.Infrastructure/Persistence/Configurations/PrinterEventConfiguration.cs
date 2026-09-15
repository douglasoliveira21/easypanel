using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="PrinterEvent"/> (R6.4, R12.3, R17.7).
/// Eventos são somente-adição; servem de fonte ao motor de alertas da Fase 3.
///
/// Índices (design "Índices principais" da Fase 2):
/// <list type="bullet">
///   <item><c>(TenantId, Type, OccurredAt)</c>: consumo por tipo/tempo.</item>
///   <item><c>(TenantId, PrinterId, OccurredAt)</c>: histórico por impressora.</item>
/// </list>
/// </summary>
internal sealed class PrinterEventConfiguration : IEntityTypeConfiguration<PrinterEvent>
{
    public void Configure(EntityTypeBuilder<PrinterEvent> builder)
    {
        builder.ToTable("PrinterEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.Detail).HasMaxLength(1000);
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.CreatedAtTicks).IsRequired();

        // Consumo por tipo/tempo (R6.4/R12.3).
        builder.HasIndex(e => new { e.TenantId, e.Type, e.OccurredAt });

        // Histórico por impressora dentro do tenant.
        builder.HasIndex(e => new { e.TenantId, e.PrinterId, e.OccurredAt });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(e => e.TenantId);

        // Cursor global do Motor_de_Alertas (Fase 3): leitura incremental
        // cross-tenant ordenada por (CreatedAtTicks, Id), portável (evita a
        // armadilha SQLite de ordenação/comparação sobre DateTimeOffset).
        builder.HasIndex(e => e.CreatedAtTicks);
    }
}
