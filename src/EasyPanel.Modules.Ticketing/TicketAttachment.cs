using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Ticketing;

/// <summary>
/// Metadados de um arquivo anexado a um <see cref="Ticket"/> (Fase 6 — R5),
/// armazenado no MinIO sob <see cref="StorageKey"/>. Uma linha só é persistida
/// depois que o objeto correspondente foi gravado com sucesso no storage.
/// </summary>
public class TicketAttachment : TenantEntity
{
    /// <summary>Chamado ao qual este anexo pertence.</summary>
    public required Guid TicketId { get; set; }

    /// <summary>Nome original do arquivo.</summary>
    public required string FileName { get; set; }

    /// <summary>Tipo MIME declarado, validado contra a allowlist (R5.2).</summary>
    public required string ContentType { get; set; }

    /// <summary>Tamanho em bytes, validado contra o limite máximo (R5.2).</summary>
    public required long SizeBytes { get; set; }

    /// <summary>
    /// Caminho do objeto no bucket, no formato
    /// <c>tickets/{tenantId}/{ticketId}/{attachmentId}-{fileName}</c> — o
    /// isolamento por tenant está no próprio caminho, não só nos metadados.
    /// </summary>
    public required string StorageKey { get; set; }

    /// <summary>Usuário que enviou o anexo.</summary>
    public required Guid UploadedByUserId { get; set; }

    /// <summary>Instante do envio.</summary>
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Cursor portável de <see cref="UploadedAt"/>.</summary>
    public long UploadedAtTicks { get; set; }
}
