namespace EasyPanel.Api.Controllers.Ticketing;

/// <summary>Projeção de saída de metadados de anexo (Fase 6 — R5).</summary>
public sealed record TicketAttachmentResponse(
    Guid Id,
    Guid TicketId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedByUserId,
    DateTimeOffset UploadedAt);
