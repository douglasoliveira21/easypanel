using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="ContractPrinter"/> (Fase 7 —
/// R2). Índice único por (Contrato, Impressora); índice reverso por
/// Impressora para a verificação de sobreposição de vigência (R2.4) e para a
/// resolução em cascata (R4).
/// </summary>
internal sealed class ContractPrinterConfiguration : IEntityTypeConfiguration<ContractPrinter>
{
    public void Configure(EntityTypeBuilder<ContractPrinter> builder)
    {
        builder.ToTable("ContractPrinters");

        builder.HasKey(cp => cp.Id);

        builder.Property(cp => cp.CreatedAt).IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(cp => cp.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(cp => cp.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cp => new { cp.TenantId, cp.ContractId, cp.PrinterId }).IsUnique();

        builder.HasIndex(cp => new { cp.TenantId, cp.PrinterId });
    }
}
