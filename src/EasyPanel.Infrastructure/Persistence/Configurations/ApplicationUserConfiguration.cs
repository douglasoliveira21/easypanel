using EasyPanel.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core de <see cref="ApplicationUser"/>.
///
/// As tabelas base do Identity são mapeadas por <c>IdentityDbContext.OnModelCreating</c>
/// (chamado primeiro no <see cref="AppDbContext"/>); esta configuração é aplicada
/// em seguida por <c>ApplyConfigurationsFromAssembly</c> para acrescentar as regras
/// específicas da plataforma sobre <c>AspNetUsers</c>.
///
/// Unicidade de email por tenant (design "Índices"): índice único composto
/// <c>(TenantId, NormalizedEmail)</c>. O índice é filtrado para
/// <c>NormalizedEmail IS NOT NULL</c>, evitando colisões espúrias entre linhas
/// sem email e permitindo múltiplos Super Admins (TenantId nulo) desde que seus
/// emails difiram — a coluna nula de TenantId não colide sob semântica SQL de
/// NULL distinto, mas o filtro mantém o índice previsível e enxuto.
/// </summary>
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.IsActive)
            .IsRequired();

        builder.Property(u => u.MfaEnabled)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        // Unicidade de email por tenant (não global). Filtrado para linhas com
        // email definido, mantendo o índice enxuto e previsível.
        builder.HasIndex(u => new { u.TenantId, u.NormalizedEmail })
            .IsUnique()
            .HasFilter("\"NormalizedEmail\" IS NOT NULL");

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(u => u.TenantId);

        // Fase 10 (Portal do Cliente): apoio à futura listagem "usuários de um
        // Cliente" e à checagem de vínculo usuário↔Cliente (R1.1).
        builder.HasIndex(u => new { u.TenantId, u.CustomerId });
    }
}
