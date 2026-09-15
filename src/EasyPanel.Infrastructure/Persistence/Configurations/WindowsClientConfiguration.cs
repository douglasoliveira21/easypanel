using EasyPanel.Modules.Monitoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="WindowsClient"/> (R3.7, R6, R17.7).
///
/// Índices (design "Índices principais" da Fase 2):
/// <list type="bullet">
///   <item><c>UNIQUE (TenantId, UniqueId)</c>: um agente por identificador dentro do tenant (R3.1).</item>
///   <item><c>(TenantId, LocationId)</c>: agentes por local (R3.2).</item>
///   <item><c>(TenantId, State)</c>: monitoramento de estado/heartbeat ausente (R6.4).</item>
/// </list>
/// </summary>
internal sealed class WindowsClientConfiguration : IEntityTypeConfiguration<WindowsClient>
{
    public void Configure(EntityTypeBuilder<WindowsClient> builder)
    {
        builder.ToTable("WindowsClients");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.UniqueId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.Hostname)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.AgentVersion)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.State)
            .IsRequired();

        builder.Property(c => c.SecretHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // Relacionamentos 1:N Customer/Location → WindowsClients (R3.2/R3.3).
        // Restrict preserva o histórico (não se apaga cliente/local com agentes).
        builder.HasOne<EasyPanel.Modules.Customers.Customer>()
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EasyPanel.Modules.Customers.Location>()
            .WithMany()
            .HasForeignKey(c => c.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unicidade do identificador do agente por tenant (R3.1).
        builder.HasIndex(c => new { c.TenantId, c.UniqueId })
            .IsUnique();

        // Agentes por local (R3.2).
        builder.HasIndex(c => new { c.TenantId, c.LocationId });

        // Monitoramento de estado/heartbeat ausente (R6.4).
        builder.HasIndex(c => new { c.TenantId, c.State });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(c => c.TenantId);
    }
}
