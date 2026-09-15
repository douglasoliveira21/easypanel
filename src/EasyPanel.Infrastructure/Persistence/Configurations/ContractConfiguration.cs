using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Contract"/> (Fase 7 — R1, R5).
/// </summary>
internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Number).IsRequired().HasMaxLength(100);
        builder.Property(c => c.StartDate).IsRequired();
        builder.Property(c => c.StartDateTicks).IsRequired();
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.Observations).HasMaxLength(1000);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.CreatedAtTicks).IsRequired();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem/filtro por Cliente e status; base da resolução em cascata (R4).
        builder.HasIndex(c => new { c.TenantId, c.CustomerId, c.Status });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(c => c.TenantId);
    }
}
