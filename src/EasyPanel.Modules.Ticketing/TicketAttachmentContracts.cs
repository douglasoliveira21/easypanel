using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Ticketing;

/// <summary>Projeção de leitura de um <see cref="TicketAttachment"/> (R5).</summary>
public sealed record TicketAttachmentDto(
    Guid Id,
    Guid TicketId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedByUserId,
    DateTimeOffset UploadedAt);

/// <summary>Metadados de um envio de anexo (R5.1/R5.2); o conteúdo é passado à parte como <see cref="Stream"/>.</summary>
public sealed record UploadTicketAttachmentRequest(string FileName, string ContentType, long SizeBytes);

/// <summary>Conteúdo de um anexo para download (R5.3): o chamador é responsável por descartar <see cref="Content"/>.</summary>
public sealed record TicketAttachmentDownload(string FileName, string ContentType, Stream Content);

/// <summary>Serviço de anexos de <see cref="Ticket"/> sobre <see cref="IFileStorage"/> (R5).</summary>
public interface ITicketAttachmentService
{
    /// <summary>Valida tamanho/tipo (R5.2), grava no storage e só então persiste os metadados (R5.1).</summary>
    Task<Result<TicketAttachmentDto>> UploadAsync(
        Guid ticketId, UploadTicketAttachmentRequest request, Stream content, CancellationToken ct);

    /// <summary>Resolve os metadados restritos ao tenant e lê o conteúdo do storage (R5.3).</summary>
    Task<Result<TicketAttachmentDownload>> DownloadAsync(Guid ticketId, Guid attachmentId, CancellationToken ct);

    /// <summary>Lista os anexos de um chamado, restrito ao tenant (R5.4).</summary>
    Task<Result<IReadOnlyList<TicketAttachmentDto>>> ListAsync(Guid ticketId, CancellationToken ct);
}
