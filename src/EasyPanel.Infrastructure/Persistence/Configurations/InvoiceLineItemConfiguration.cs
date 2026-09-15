using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="InvoiceLineItem"/> (Fase 8 —
/// R4.3). Somente-adição; imutável depois que a Fatura sai de Rascunho.
/// </summary>
internal sealed class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.ToTable("InvoiceLineItems");

        builder.HasKey(li => li.Id);

        builder.Property(li => li.CounterType).IsRequired();
        builder.Property(li => li.CounterTypeLabel).HasMaxLength(100);
        builder.Property(li => li.ConsumedQuantity).IsRequired();
        builder.Property(li => li.IncludedQuantity).IsRequired();
        builder.Property(li => li.ExcessQuantity).IsRequired();
        builder.Property(li => li.UnitPrice).IsRequired().HasPrecision(18, 4);
        builder.Property(li => li.LineAmount).IsRequired().HasPrecision(18, 2);
        builder.Property(li => li.CreatedAt).IsRequired();

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(li => li.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(li => li.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(li => new { li.TenantId, li.InvoiceId });
    }
}
