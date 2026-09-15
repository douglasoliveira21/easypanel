using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Invoice"/> (Fase 8 — R4/R5).
/// </summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.PeriodStart).IsRequired();
        builder.Property(i => i.PeriodStartTicks).IsRequired();
        builder.Property(i => i.PeriodEnd).IsRequired();
        builder.Property(i => i.PeriodEndTicks).IsRequired();
        builder.Property(i => i.Status).IsRequired();
        builder.Property(i => i.TotalAmount).IsRequired().HasPrecision(18, 2);
        builder.Property(i => i.Currency).IsRequired().HasMaxLength(3);
        builder.Property(i => i.GeneratedAt).IsRequired();
        builder.Property(i => i.GeneratedAtTicks).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(i => i.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem/filtro (R6.1).
        builder.HasIndex(i => new { i.TenantId, i.CustomerId });
        builder.HasIndex(i => new { i.TenantId, i.ContractId });
        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.PeriodStartTicks, i.PeriodEndTicks });
    }
}
