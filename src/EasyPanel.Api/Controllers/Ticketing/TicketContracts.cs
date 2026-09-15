using EasyPanel.Modules.Ticketing;

namespace EasyPanel.Api.Controllers.Ticketing;

/// <summary>Página baseada em cursor para o histórico de interações (Fase 6 — R2.6).</summary>
public sealed record TicketCursorPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de saída de um <see cref="Ticket"/> (R1, R3, R4).</summary>
public sealed record TicketResponse(
    Guid Id,
    Guid TenantId,
    string Title,
    string? Description,
    Guid CustomerId,
    Guid? LocationId,
    Guid? PrinterId,
    TicketPriority Priority,
    TicketStatus Status,
    Guid RequestedByUserId,
    Guid? AssignedToUserId,
    DateTimeOffset FirstResponseDueAt,
    DateTimeOffset? FirstResponseAt,
    SlaComplianceStatus FirstResponseCompliance,
    DateTimeOffset ResolutionDueAt,
    DateTimeOffset? ResolvedAt,
    SlaComplianceStatus ResolutionCompliance,
    DateTimeOffset CreatedAt);

/// <summary>Página de resultados da listagem de chamados (R3.1, R12.4).</summary>
public sealed record TicketPageResponse(IReadOnlyList<TicketResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>Projeção de saída de uma <see cref="TicketInteraction"/> (R2.5/R2.6).</summary>
public sealed record TicketInteractionResponse(
    Guid Id,
    Guid TicketId,
    TicketInteractionType Type,
    Guid ActorUserId,
    string? Comment,
    TicketStatus? FromStatus,
    TicketStatus? ToStatus,
    Guid? AssignedToUserId,
    DateTimeOffset OccurredAt);

/// <summary>Corpo da requisição de abertura de um chamado (R1.1/R1.2).</summary>
public sealed record CreateTicketApiRequest(
    string Title,
    string? Description,
    Guid CustomerId,
    Guid? LocationId,
    Guid? PrinterId,
    TicketPriority Priority);

/// <summary>Corpo da requisição de transição de status (R2.2/R2.3).</summary>
public sealed record ChangeTicketStatusApiRequest(TicketStatus ToStatus);

/// <summary>Corpo da requisição de atribuição/desatribuição (R2.4).</summary>
public sealed record AssignTicketApiRequest(Guid? AssignedToUserId);

/// <summary>Corpo da requisição de um comentário (R2.5).</summary>
public sealed record AddTicketCommentApiRequest(string Comment);
