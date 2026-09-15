using EasyPanel.Modules.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Location"/> (R9.2, R9.3, R12.5).
///
/// Comprimentos e obrigatoriedade dos campos textuais. O relacionamento com
/// <see cref="Customer"/> é 1:N (um cliente possui múltiplos locais — R9.3), com
/// <c>Restrict</c> na exclusão para preservar o histórico.
///
/// Índices (design "Índices principais"):
/// <list type="bullet">
///   <item><c>(TenantId, CustomerId)</c>: listagem de locais por cliente (R9.7).</item>
///   <item><c>(TenantId, Status)</c>: filtro por status dentro do tenant (R12.5).</item>
/// </list>
/// Um índice isolado por <c>TenantId</c> é adicionado por consistência com o
/// design; a ordenação por <c>Nome</c> é a chave de ordenação determinística e
/// portável da listagem.
/// </summary>
internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Nome)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(l => l.Endereco)
            .HasMaxLength(300);

        builder.Property(l => l.Responsavel)
            .HasMaxLength(200);

        builder.Property(l => l.Telefone)
            .HasMaxLength(30);

        builder.Property(l => l.Email)
            .HasMaxLength(256);

        builder.Property(l => l.Observacoes)
            .HasMaxLength(2000);

        builder.Property(l => l.Status)
            .IsRequired();

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        // Relacionamento 1:N Customer → Locations (R9.3). Restrict preserva o
        // histórico: não se exclui um cliente com locais em cascata.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem de locais por cliente dentro do tenant (R9.7).
        builder.HasIndex(l => new { l.TenantId, l.CustomerId });

        // Filtro por status dentro do tenant (R12.5).
        builder.HasIndex(l => new { l.TenantId, l.Status });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(l => l.TenantId);
    }
}
