using EasyPanel.Modules.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="AuditLog"/> — a trilha de auditoria
/// somente-adição (R10.1, R10.4).
///
/// Índices (design "Índices principais"):
/// <list type="bullet">
///   <item>
///     <c>(TenantId, OccurredAt DESC)</c>: consulta paginada por tenant ordenada do
///     evento mais recente para o mais antigo (R10.5), o padrão de listagem de
///     auditoria.
///   </item>
///   <item>
///     <c>(TenantId, ResourceType)</c>: filtro por tipo de recurso dentro de um
///     tenant.
///   </item>
/// </list>
///
/// Comprimentos máximos são definidos nos campos textuais curtos; <c>OldValues</c>/
/// <c>NewValues</c> ficam como <c>text</c> por carregarem JSON de tamanho variável
/// (portável entre PostgreSQL e o SQLite usado em testes).
/// </summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.ResourceType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.ResourceId)
            .HasMaxLength(128);

        builder.Property(a => a.Ip)
            .HasMaxLength(64);

        builder.Property(a => a.UserAgent)
            .HasMaxLength(512);

        builder.Property(a => a.OldValues)
            .HasColumnType("text");

        builder.Property(a => a.NewValues)
            .HasColumnType("text");

        // Result é enum persistido como int pela convenção do AppDbContext; a
        // coluna é obrigatória.
        builder.Property(a => a.Result)
            .IsRequired();

        builder.Property(a => a.OccurredAt)
            .IsRequired();

        // Índice principal de listagem: eventos de um tenant, do mais recente ao
        // mais antigo (R10.5).
        builder.HasIndex(a => new { a.TenantId, a.OccurredAt })
            .IsDescending(false, true);

        // Filtro por tipo de recurso dentro do tenant.
        builder.HasIndex(a => new { a.TenantId, a.ResourceType });
    }
}
