using EasyPanel.Modules.Portal;

namespace EasyPanel.Api.Controllers.Portal;

/// <summary>Página baseada em cursor para as listagens do Portal do Cliente (Fase 10).</summary>
public sealed record PortalCursorPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de saída de uma Impressora no Portal do Cliente (Fase 10 — R3.1).</summary>
public sealed record PortalPrinterResponse(
    Guid Id,
    string? Fabricante,
    string? Modelo,
    string? NumeroSerie,
    Guid LocationId,
    string LocationName,
    PortalPrinterStatus Status);

/// <summary>Leitura de contador no Portal do Cliente (Fase 10 — R3.1).</summary>
public sealed record PortalCounterReadingResponse(DateTimeOffset Timestamp, string CounterType, long Value);

/// <summary>Resumo de Chamado no Portal do Cliente (Fase 10 — R4.1).</summary>
public sealed record PortalTicketSummaryResponse(
    Guid Id, string Title, PortalTicketStatus Status, DateTimeOffset CreatedAt);

/// <summary>Comentário do histórico de um Chamado no Portal do Cliente (Fase 10 — R4.2).</summary>
public sealed record PortalTicketCommentResponse(DateTimeOffset OccurredAt, string Comment);

/// <summary>Anexo de um Chamado no Portal do Cliente (Fase 10 — R4.2).</summary>
public sealed record PortalTicketAttachmentResponse(Guid Id, string FileName, string ContentType);

/// <summary>Detalhe de Chamado no Portal do Cliente (Fase 10 — R4.2).</summary>
public sealed record PortalTicketDetailResponse(
    Guid Id,
    string Title,
    string? Description,
    PortalTicketStatus Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PortalTicketCommentResponse> Comments,
    IReadOnlyList<PortalTicketAttachmentResponse> Attachments);

/// <summary>Resumo de Fatura no Portal do Cliente (Fase 10 — R5.1).</summary>
public sealed record PortalInvoiceSummaryResponse(
    Guid Id,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    PortalInvoiceStatus Status,
    decimal TotalAmount);

/// <summary>Item de uma Fatura no Portal do Cliente (Fase 10 — R5.3).</summary>
public sealed record PortalInvoiceLineItemResponse(
    Guid PrinterId,
    string CounterType,
    long ConsumedQuantity,
    long IncludedQuantity,
    long ExcessQuantity,
    decimal UnitPrice,
    decimal LineAmount);

/// <summary>Detalhe de Fatura no Portal do Cliente (Fase 10 — R5.3).</summary>
public sealed record PortalInvoiceDetailResponse(
    Guid Id,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    PortalInvoiceStatus Status,
    decimal TotalAmount,
    IReadOnlyList<PortalInvoiceLineItemResponse> LineItems);
