using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Ticketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="Ticket"/> (Fase 6 — R1, R2, R4).
/// Cursor de listagem por <c>CreatedAtTicks</c> (portável); prazos de SLA
/// também com colunas <c>*Ticks</c> irmãs.
/// </summary>
internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.Priority).IsRequired();
        builder.Property(t => t.Status).IsRequired();
        builder.Property(t => t.RequestedByUserId).IsRequired();
        builder.Property(t => t.FirstResponseDueAt).IsRequired();
        builder.Property(t => t.FirstResponseDueAtTicks).IsRequired();
        builder.Property(t => t.FirstResponseCompliance).IsRequired();
        builder.Property(t => t.ResolutionDueAt).IsRequired();
        builder.Property(t => t.ResolutionDueAtTicks).IsRequired();
        builder.Property(t => t.ResolutionCompliance).IsRequired();
        builder.Property(t => t.CreatedAtTicks).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(t => t.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(t => t.PrinterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem/filtro por status, mais recentes primeiro (R3.1/R3.2).
        builder.HasIndex(t => new { t.TenantId, t.Status, t.CreatedAtTicks });

        // Filtro por Cliente (R3.2).
        builder.HasIndex(t => new { t.TenantId, t.CustomerId });

        // Filtro por Técnico responsável (R3.2).
        builder.HasIndex(t => new { t.TenantId, t.AssignedToUserId });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(t => t.TenantId);
    }
}
