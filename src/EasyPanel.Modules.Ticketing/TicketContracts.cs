using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Ticketing;

/// <summary>Página baseada em cursor para grandes volumes (R2.6), específica deste módulo
/// para não referenciar tipos de cursor de outros módulos.</summary>
public sealed record TicketCursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de leitura de um <see cref="Ticket"/> (R1, R3).</summary>
public sealed record TicketDto(
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

/// <summary>Projeção de leitura de uma <see cref="TicketInteraction"/> (R2.5/R2.6).</summary>
public sealed record TicketInteractionDto(
    Guid Id,
    Guid TicketId,
    TicketInteractionType Type,
    Guid ActorUserId,
    string? Comment,
    TicketStatus? FromStatus,
    TicketStatus? ToStatus,
    Guid? AssignedToUserId,
    DateTimeOffset OccurredAt);

/// <summary>Dados de abertura de um <see cref="Ticket"/> (R1.1/R1.2). Tenant/solicitante vêm do contexto.</summary>
public sealed record CreateTicketRequest(
    string Title,
    string? Description,
    Guid CustomerId,
    Guid? LocationId,
    Guid? PrinterId,
    TicketPriority Priority);

/// <summary>Filtro de listagem de <see cref="Ticket"/> (R3.2).</summary>
public sealed record TicketQuery(
    PageRequest Page,
    TicketStatus? Status = null,
    TicketPriority? Priority = null,
    Guid? CustomerId = null,
    Guid? LocationId = null,
    Guid? PrinterId = null,
    Guid? AssignedToUserId = null);

/// <summary>Dados de transição de status de um <see cref="Ticket"/> (R2.2/R2.3).</summary>
public sealed record ChangeTicketStatusRequest(TicketStatus ToStatus);

/// <summary>Dados de atribuição/reatribuição de um <see cref="Ticket"/> (R2.4). <c>null</c> desatribui.</summary>
public sealed record AssignTicketRequest(Guid? AssignedToUserId);

/// <summary>Dados de um comentário registrado num <see cref="Ticket"/> (R2.5).</summary>
public sealed record AddTicketCommentRequest(string Comment);

/// <summary>Serviço de ciclo de vida de <see cref="Ticket"/> (R1/R2/R3/R4).</summary>
public interface ITicketService
{
    /// <summary>Abre um chamado, calculando os prazos de SLA da prioridade informada (R1.2/R1.5).</summary>
    Task<Result<TicketDto>> CreateAsync(CreateTicketRequest request, CancellationToken ct);

    /// <summary>Consulta por id, restrito ao tenant (R3.3).</summary>
    Task<Result<TicketDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lista chamados com filtros/paginação, restrito ao tenant (R3.1/R3.2).</summary>
    Task<Result<PagedResult<TicketDto>>> ListAsync(TicketQuery query, CancellationToken ct);

    /// <summary>Transiciona o status conforme a máquina de estados (R2.2/R2.3), registra Interação e,
    /// quando aplicável, marca a resolução (R2.7/R4.5).</summary>
    Task<Result<TicketDto>> ChangeStatusAsync(Guid id, ChangeTicketStatusRequest request, CancellationToken ct);

    /// <summary>Atribui/reatribui/desatribui o chamado, registrando Interação (R2.4).</summary>
    Task<Result<TicketDto>> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct);

    /// <summary>Registra um comentário e, quando é a primeira resposta de um Técnico, marca
    /// o cumprimento do SLA de primeira resposta (R2.5/R4.4).</summary>
    Task<Result<TicketInteractionDto>> AddCommentAsync(Guid id, AddTicketCommentRequest request, CancellationToken ct);

    /// <summary>Histórico de interações do chamado por cursor pagination (R2.6).</summary>
    Task<Result<TicketCursorPage<TicketInteractionDto>>> ListInteractionsAsync(
        Guid ticketId, string? cursor, int pageSize, CancellationToken ct);

    /// <summary>Lista os chamados do tenant com SLA violado ou já vencido (R4.6).</summary>
    Task<Result<PagedResult<TicketDto>>> ListSlaBreachedAsync(PageRequest page, CancellationToken ct);
}
