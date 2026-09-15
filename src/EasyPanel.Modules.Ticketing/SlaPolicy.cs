using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Ticketing;

/// <summary>
/// Política de SLA do Tenant para uma <see cref="TicketPriority"/> (Fase 6 —
/// R4.1): prazos de primeira resposta e de resolução, em minutos. Uma linha
/// por (Tenant, Prioridade) — upsert. Ausência de linha para uma prioridade
/// usa o prazo padrão de plataforma (R4.2).
/// </summary>
public class SlaPolicy : TenantEntity
{
    /// <summary>Prioridade a que esta política se aplica.</summary>
    public required TicketPriority Priority { get; set; }

    /// <summary>Prazo de primeira resposta, em minutos (&gt; 0).</summary>
    public required int FirstResponseMinutes { get; set; }

    /// <summary>Prazo de resolução, em minutos (&gt; 0, e &gt;= <see cref="FirstResponseMinutes"/>).</summary>
    public required int ResolutionMinutes { get; set; }
}
