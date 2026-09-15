using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="PrinterCounter"/> (R11, R17.7).
/// Leituras são somente-adição.
///
/// Índices (design "Índices principais" da Fase 2):
/// <list type="bullet">
///   <item>
///     <c>(TenantId, PrinterId, CounterType, Timestamp)</c>: chave de ordenação e
///     cursor para consulta de histórico e validação de não-decréscimo (R11.5/R11.7).
///   </item>
/// </list>
/// </summary>
internal sealed class PrinterCounterConfiguration : IEntityTypeConfiguration<PrinterCounter>
{
    public void Configure(EntityTypeBuilder<PrinterCounter> builder)
    {
        builder.ToTable("PrinterCounters");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Timestamp).IsRequired();
        builder.Property(c => c.TimestampTicks).IsRequired();
        builder.Property(c => c.CounterType).IsRequired();
        builder.Property(c => c.CounterTypeLabel).HasMaxLength(64);
        builder.Property(c => c.Value).IsRequired();
        builder.Property(c => c.Source).IsRequired();
        builder.Property(c => c.IsAdministrativeAdjustment).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // Relacionamento 1:N Printer → PrinterCounters (R11.1).
        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(c => c.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chave de cursor/ordenação (por ticks, portável) e apoio à validação de
        // não-decréscimo (R11.5/R11.7).
        builder.HasIndex(c => new { c.TenantId, c.PrinterId, c.CounterType, c.TimestampTicks });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(c => c.TenantId);
    }
}
