namespace EasyPanel.Modules.Ticketing;

/// <summary>Estado do ciclo de vida de um <see cref="Ticket"/> (Fase 6 — R2.1). Transições
/// permitidas são impostas pelo serviço, não pelo enum — ver <c>design.md</c>.</summary>
public enum TicketStatus
{
    Aberto = 0,
    EmAndamento = 1,
    AguardandoCliente = 2,
    Resolvido = 3,
    Fechado = 4,
    Cancelado = 5,
}

/// <summary>Prioridade de um <see cref="Ticket"/>, usada na resolução da <see cref="SlaPolicy"/> (Fase 6 — R4).</summary>
public enum TicketPriority
{
    Baixa = 0,
    Media = 1,
    Alta = 2,
    Urgente = 3,
}

/// <summary>Tipo de uma <see cref="TicketInteraction"/> no histórico somente-adição (Fase 6 — R2.5/R2.6).</summary>
public enum TicketInteractionType
{
    Comentario = 0,
    MudancaStatus = 1,
    Atribuicao = 2,
}

/// <summary>Estado de cumprimento de um prazo de SLA (Fase 6 — R4.4/R4.5).</summary>
public enum SlaComplianceStatus
{
    Pendente = 0,
    Cumprido = 1,
    Violado = 2,
}
