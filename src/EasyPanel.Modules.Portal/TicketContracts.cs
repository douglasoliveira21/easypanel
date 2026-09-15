using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Portal;

/// <summary>
/// Resumo de Chamado exposto pelo Portal do Cliente (Fase 10 — R4.1), restrito
/// ao Cliente do Escopo de Cliente.
/// </summary>
public sealed record PortalTicketSummaryDto(
    Guid Id, string Title, PortalTicketStatus Status, DateTimeOffset CreatedAt);

/// <summary>
/// Comentário do histórico de um Chamado, exposto pelo Portal do Cliente
/// (Fase 10 — R4.2). Apenas interações do tipo comentário são expostas — mudanças
/// de status/atribuição são detalhe operacional interno, não pertinente à
/// visão do Cliente (o <see cref="PortalTicketDetailDto.Status"/> já reflete o
/// estado corrente).
/// </summary>
public sealed record PortalTicketCommentDto(DateTimeOffset OccurredAt, string Comment);

/// <summary>Anexo de um Chamado exposto pelo Portal do Cliente (Fase 10 — R4.2).</summary>
public sealed record PortalTicketAttachmentDto(Guid Id, string FileName, string ContentType);

/// <summary>
/// Detalhe de Chamado exposto pelo Portal do Cliente (Fase 10 — R4.2), com
/// histórico de comentários e anexos.
/// </summary>
public sealed record PortalTicketDetailDto(
    Guid Id,
    string Title,
    string? Description,
    PortalTicketStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PortalTicketCommentDto> Comments,
    IReadOnlyList<PortalTicketAttachmentDto> Attachments);

/// <summary>
/// Leitura de Chamados do Cliente do Escopo de Cliente (Fase 10 — R4),
/// somente-leitura (nenhuma criação/interação pelo portal nesta fase — R4.3).
/// </summary>
public interface IPortalTicketService
{
    /// <summary>Lista os Chamados do Cliente do Escopo de Cliente, por cursor (R4.1).</summary>
    Task<Result<PortalCursorPage<PortalTicketSummaryDto>>> ListAsync(
        string? cursor, int pageSize, CancellationToken ct);

    /// <summary>
    /// Detalha um Chamado, com comentários e anexos. Retorna
    /// <see cref="PortalErrors.NotFound"/> se não pertencer ao Cliente do Escopo
    /// de Cliente (R4.2).
    /// </summary>
    Task<Result<PortalTicketDetailDto>> GetAsync(Guid ticketId, CancellationToken ct);
}
