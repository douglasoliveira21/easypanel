using EasyPanel.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="BillingClosing"/> (Fase 8 —
/// R1). Índice único por (Tenant, Ano, Mês) — impede refechamento
/// silencioso (R1.3) na própria constraint de banco.
/// </summary>
internal sealed class BillingClosingConfiguration : IEntityTypeConfiguration<BillingClosing>
{
    public void Configure(EntityTypeBuilder<BillingClosing> builder)
    {
        builder.ToTable("BillingClosings");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Year).IsRequired();
        builder.Property(c => c.Month).IsRequired();
        builder.Property(c => c.PeriodStart).IsRequired();
        builder.Property(c => c.PeriodStartTicks).IsRequired();
        builder.Property(c => c.PeriodEnd).IsRequired();
        builder.Property(c => c.PeriodEndTicks).IsRequired();
        builder.Property(c => c.ExecutedAt).IsRequired();
        builder.Property(c => c.ExecutedAtTicks).IsRequired();
        builder.Property(c => c.InvoiceCount).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Year, c.Month }).IsUnique();
    }
}
