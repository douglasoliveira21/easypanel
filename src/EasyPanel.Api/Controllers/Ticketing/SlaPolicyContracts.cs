using EasyPanel.Modules.Ticketing;

namespace EasyPanel.Api.Controllers.Ticketing;

/// <summary>Projeção de saída de uma política de SLA (Fase 6 — R4).</summary>
public sealed record SlaPolicyResponse(Guid Id, TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);

/// <summary>Corpo da requisição de upsert de política de SLA (R4.1).</summary>
public sealed record SetSlaPolicyApiRequest(TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);
