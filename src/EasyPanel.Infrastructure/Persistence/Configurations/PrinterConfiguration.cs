using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Printer"/> (R9.1, R9.5, R17.7).
///
/// Índices (design "Índices principais" da Fase 2):
/// <list type="bullet">
///   <item><c>(TenantId, CustomerId)</c> e <c>(TenantId, LocationId)</c>: listagem do parque (R9.5).</item>
///   <item><c>(TenantId, Status)</c>: filtro por status (R9.2/R9.5).</item>
///   <item><c>(TenantId, NumeroSerie)</c>: busca por número de série (R9.5).</item>
/// </list>
/// </summary>
internal sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> builder)
    {
        builder.ToTable("Printers");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Fabricante).HasMaxLength(120);
        builder.Property(p => p.Modelo).HasMaxLength(120);
        builder.Property(p => p.NumeroSerie).HasMaxLength(120);
        builder.Property(p => p.Patrimonio).HasMaxLength(120);
        builder.Property(p => p.Ip).HasMaxLength(64);
        builder.Property(p => p.Mac).HasMaxLength(32);
        builder.Property(p => p.Hostname).HasMaxLength(256);
        builder.Property(p => p.Protocolo).HasMaxLength(32);
        builder.Property(p => p.Observacoes).HasMaxLength(2000);

        builder.Property(p => p.Status).IsRequired();
        builder.Property(p => p.MonitoringEnabled).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();

        // Relacionamentos 1:N Customer/Location → Printers (R9.1). Restrict
        // preserva o histórico do parque.
        builder.HasOne<EasyPanel.Modules.Customers.Customer>()
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EasyPanel.Modules.Customers.Location>()
            .WithMany()
            .HasForeignKey(p => p.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem do parque por cliente/local dentro do tenant (R9.5).
        builder.HasIndex(p => new { p.TenantId, p.CustomerId });
        builder.HasIndex(p => new { p.TenantId, p.LocationId });

        // Filtro por status dentro do tenant (R9.2/R9.5).
        builder.HasIndex(p => new { p.TenantId, p.Status });

        // Busca por número de série dentro do tenant (R9.5).
        builder.HasIndex(p => new { p.TenantId, p.NumeroSerie });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(p => p.TenantId);
    }
}
