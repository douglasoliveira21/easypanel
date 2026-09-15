using EasyPanel.Modules.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Customer"/> (R8.2, R8.5, R12.5).
///
/// Comprimentos e obrigatoriedade dos campos textuais; o CNPJ é
/// <c>char(14)</c> por ser sempre normalizado (somente dígitos).
///
/// Índices (design "Índices principais"):
/// <list type="bullet">
///   <item><c>UNIQUE (TenantId, Cnpj)</c>: unicidade de CNPJ por tenant (R8.5).</item>
///   <item><c>(TenantId, Status)</c>: filtro de listagem por status (R12.5).</item>
///   <item><c>(TenantId, RazaoSocial)</c>: filtro/ordenação por razão social
///     (R12.5) — coluna usada como ordenação determinística e portável na
///     listagem.</item>
/// </list>
/// O índice de apoio ao isolamento por <c>(TenantId)</c> é coberto pelos índices
/// compostos que iniciam por <c>TenantId</c>; adiciona-se um índice isolado por
/// <c>TenantId</c> explicitamente por consistência com o design.
/// </summary>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.RazaoSocial)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.NomeFantasia)
            .HasMaxLength(200);

        // CNPJ normalizado: sempre 14 dígitos.
        builder.Property(c => c.Cnpj)
            .IsRequired()
            .HasMaxLength(14)
            .IsFixedLength();

        builder.Property(c => c.InscricaoEstadual)
            .HasMaxLength(30);

        builder.Property(c => c.Telefone)
            .HasMaxLength(30);

        builder.Property(c => c.Email)
            .HasMaxLength(256);

        builder.Property(c => c.Endereco)
            .HasMaxLength(300);

        builder.Property(c => c.Cidade)
            .HasMaxLength(120);

        builder.Property(c => c.Estado)
            .HasMaxLength(2)
            .IsFixedLength();

        builder.Property(c => c.Cep)
            .HasMaxLength(9);

        builder.Property(c => c.Observacoes)
            .HasMaxLength(2000);

        // Status é enum persistido como int pela convenção do AppDbContext.
        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // Unicidade de CNPJ por tenant (R8.5) → conflito 409.
        builder.HasIndex(c => new { c.TenantId, c.Cnpj })
            .IsUnique();

        // Filtro por status dentro do tenant (R12.5).
        builder.HasIndex(c => new { c.TenantId, c.Status });

        // Filtro/ordenação por razão social dentro do tenant (R12.5).
        builder.HasIndex(c => new { c.TenantId, c.RazaoSocial });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(c => c.TenantId);
    }
}
