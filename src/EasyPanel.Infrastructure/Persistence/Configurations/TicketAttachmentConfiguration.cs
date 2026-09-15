using EasyPanel.Modules.Ticketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyPanel.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuração EF Core da entidade <see cref="TicketAttachment"/> (Fase 6 —
/// R5). Uma linha só é persistida depois que o objeto correspondente foi
/// gravado com sucesso no storage (ver <c>TicketAttachmentService</c>).
/// </summary>
internal sealed class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("TicketAttachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.SizeBytes).IsRequired();
        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(500);
        builder.Property(a => a.UploadedByUserId).IsRequired();
        builder.Property(a => a.UploadedAt).IsRequired();
        builder.Property(a => a.UploadedAtTicks).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // Listagem de anexos por chamado (R5.4).
        builder.HasIndex(a => new { a.TenantId, a.TicketId });
    }
}
