using EasyPanel.Modules.Ticketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="TicketInteraction"/> (Fase 6 —
/// R2.5/R2.6). Somente-adição; cursor de histórico por <c>OccurredAtTicks</c>.
/// </summary>
internal sealed class TicketInteractionConfiguration : IEntityTypeConfiguration<TicketInteraction>
{
    public void Configure(EntityTypeBuilder<TicketInteraction> builder)
    {
        builder.ToTable("TicketInteractions");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Type).IsRequired();
        builder.Property(i => i.ActorUserId).IsRequired();
        builder.Property(i => i.Comment).HasMaxLength(4000);
        builder.Property(i => i.OccurredAt).IsRequired();
        builder.Property(i => i.OccurredAtTicks).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(i => i.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // Histórico por chamado, cursor pagination por OccurredAtTicks (R2.6).
        builder.HasIndex(i => new { i.TenantId, i.TicketId, i.OccurredAtTicks });

        // Índice de apoio ao isolamento/consulta por tenant.
        builder.HasIndex(i => i.TenantId);
    }
}
