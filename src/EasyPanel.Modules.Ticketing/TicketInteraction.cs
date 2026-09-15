using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Ticketing;

/// <summary>
/// Entrada somente-adição no histórico de um <see cref="Ticket"/>: comentário,
/// mudança de status ou atribuição (Fase 6 — R2.5/R2.6). Nunca alterada ou
/// removida após criada.
/// </summary>
public class TicketInteraction : TenantEntity
{
    /// <summary>Chamado ao qual esta interação pertence.</summary>
    public required Guid TicketId { get; set; }

    /// <summary>Tipo da interação.</summary>
    public required TicketInteractionType Type { get; set; }

    /// <summary>Autor da interação.</summary>
    public required Guid ActorUserId { get; set; }

    /// <summary>Texto do comentário — obrigatório sse <see cref="Type"/> = <see cref="TicketInteractionType.Comentario"/>.</summary>
    public string? Comment { get; set; }

    /// <summary>Status de origem — preenchido sse <see cref="Type"/> = <see cref="TicketInteractionType.MudancaStatus"/>.</summary>
    public TicketStatus? FromStatus { get; set; }

    /// <summary>Status de destino — preenchido sse <see cref="Type"/> = <see cref="TicketInteractionType.MudancaStatus"/>.</summary>
    public TicketStatus? ToStatus { get; set; }

    /// <summary>Novo responsável — preenchido sse <see cref="Type"/> = <see cref="TicketInteractionType.Atribuicao"/>;
    /// <c>null</c> representa desatribuição.</summary>
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Instante da interação.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Cursor portável de <see cref="OccurredAt"/> (R2.6).</summary>
    public long OccurredAtTicks { get; set; }
}
