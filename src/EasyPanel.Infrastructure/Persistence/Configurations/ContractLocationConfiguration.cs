using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="ContractLocation"/> (Fase 7 —
/// R2). Índice único por (Contrato, Local); índice reverso por Local para a
/// verificação de sobreposição de vigência (R2.4) e para a resolução em
/// cascata (R4).
/// </summary>
internal sealed class ContractLocationConfiguration : IEntityTypeConfiguration<ContractLocation>
{
    public void Configure(EntityTypeBuilder<ContractLocation> builder)
    {
        builder.ToTable("ContractLocations");

        builder.HasKey(cl => cl.Id);

        builder.Property(cl => cl.CreatedAt).IsRequired();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(cl => cl.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(cl => cl.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cl => new { cl.TenantId, cl.ContractId, cl.LocationId }).IsUnique();

        builder.HasIndex(cl => new { cl.TenantId, cl.LocationId });
    }
}
