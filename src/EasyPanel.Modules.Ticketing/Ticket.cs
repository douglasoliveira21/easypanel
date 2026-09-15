using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Ticketing;

/// <summary>
/// Chamado aberto por/para um Cliente, com ciclo de vida de status e prazos de
/// SLA calculados na abertura (Fase 6 — R1, R2, R4). Cliente, Local, Impressora
/// e usuários (solicitante/atribuído) são referenciados apenas por <c>Guid</c>.
/// </summary>
public class Ticket : TenantEntity
{
    /// <summary>Título curto do chamado.</summary>
    public required string Title { get; set; }

    /// <summary>Descrição detalhada.</summary>
    public string? Description { get; set; }

    /// <summary>Cliente ao qual o chamado se refere (R1.3).</summary>
    public required Guid CustomerId { get; set; }

    /// <summary>Local, quando aplicável (R1.4).</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Impressora, quando aplicável (R1.4).</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>Prioridade, usada na resolução da <see cref="SlaPolicy"/> (R1.5).</summary>
    public TicketPriority Priority { get; set; } = TicketPriority.Media;

    /// <summary>Status corrente — desnormalizado a partir da última <see cref="TicketInteraction"/>
    /// do tipo <see cref="TicketInteractionType.MudancaStatus"/>, para consulta/filtro (R3.2).</summary>
    public TicketStatus Status { get; set; } = TicketStatus.Aberto;

    /// <summary>Usuário que abriu o chamado.</summary>
    public required Guid RequestedByUserId { get; set; }

    /// <summary>Técnico responsável, quando atribuído.</summary>
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Prazo-limite de primeira resposta, calculado na abertura (R1.5/R4.3).</summary>
    public DateTimeOffset FirstResponseDueAt { get; set; }

    /// <summary>Cursor portável de <see cref="FirstResponseDueAt"/>.</summary>
    public long FirstResponseDueAtTicks { get; set; }

    /// <summary>Timestamp da primeira resposta de um Técnico (R4.4).</summary>
    public DateTimeOffset? FirstResponseAt { get; set; }

    /// <summary>Cursor portável de <see cref="FirstResponseAt"/>.</summary>
    public long? FirstResponseAtTicks { get; set; }

    /// <summary>Cumprimento do prazo de primeira resposta (R4.4).</summary>
    public SlaComplianceStatus FirstResponseCompliance { get; set; } = SlaComplianceStatus.Pendente;

    /// <summary>Prazo-limite de resolução, calculado na abertura (R1.5/R4.3).</summary>
    public DateTimeOffset ResolutionDueAt { get; set; }

    /// <summary>Cursor portável de <see cref="ResolutionDueAt"/>.</summary>
    public long ResolutionDueAtTicks { get; set; }

    /// <summary>Timestamp da primeira transição para <see cref="TicketStatus.Resolvido"/> (R2.7).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Cursor portável de <see cref="ResolvedAt"/>.</summary>
    public long? ResolvedAtTicks { get; set; }

    /// <summary>Cumprimento do prazo de resolução (R4.5).</summary>
    public SlaComplianceStatus ResolutionCompliance { get; set; } = SlaComplianceStatus.Pendente;

    /// <summary>Cursor portável de <c>CreatedAt</c> (herdado de <see cref="BaseEntity"/>), para listagem (R3.1).</summary>
    public long CreatedAtTicks { get; set; }
}
