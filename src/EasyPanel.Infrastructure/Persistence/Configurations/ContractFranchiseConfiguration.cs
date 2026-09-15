using EasyPanel.Modules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="ContractFranchise"/> (Fase 7 —
/// R3). Índice único por (Contrato, CounterType) — upsert.
/// </summary>
internal sealed class ContractFranchiseConfiguration : IEntityTypeConfiguration<ContractFranchise>
{
    public void Configure(EntityTypeBuilder<ContractFranchise> builder)
    {
        builder.ToTable("ContractFranchises");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.CounterType).IsRequired();
        builder.Property(f => f.CounterTypeLabel).HasMaxLength(100);
        builder.Property(f => f.IncludedQuantity).IsRequired();
        builder.Property(f => f.ExcessUnitPrice).IsRequired().HasPrecision(18, 4);
        builder.Property(f => f.Currency).IsRequired().HasMaxLength(3);
        builder.Property(f => f.CreatedAt).IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(f => f.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => new { f.TenantId, f.ContractId, f.CounterType }).IsUnique();
    }
}
