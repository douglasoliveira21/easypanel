using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Ocorrência de alerta gerada pelo Motor_de_Alertas a partir de uma
/// <see cref="AlertRule"/> (R3). No máximo um <see cref="Alert"/> em estado
/// <see cref="AlertState.Open"/> ou <see cref="AlertState.Acknowledged"/> existe
/// simultaneamente por <c>(TenantId, AlertRuleId, PrinterId, WindowsClientId)</c> —
/// verificado em código pelo motor de avaliação antes de inserir (R2.5).
/// </summary>
public class Alert : TenantEntity
{
    /// <summary>Regra que originou este Alerta.</summary>
    public Guid AlertRuleId { get; set; }

    /// <summary>
    /// Severidade no momento da criação (cópia da regra: a regra pode mudar depois
    /// sem afetar Alertas já existentes).
    /// </summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>Estado atual do ciclo de vida (R3.2).</summary>
    public AlertState State { get; set; } = AlertState.Open;

    /// <summary>Impressora relacionada, quando aplicável.</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>Agente relacionado, quando aplicável.</summary>
    public Guid? WindowsClientId { get; set; }

    /// <summary>Momento da primeira ocorrência correspondente (UTC).</summary>
    public DateTimeOffset FirstOccurrenceAt { get; set; }

    /// <summary>Ticks (UTC) de <see cref="FirstOccurrenceAt"/>.</summary>
    public long FirstOccurrenceAtTicks { get; set; }

    /// <summary>Momento da última ocorrência correspondente (UTC).</summary>
    public DateTimeOffset LastOccurrenceAt { get; set; }

    /// <summary>Ticks (UTC) de <see cref="LastOccurrenceAt"/>. Usado como cursor de listagem.</summary>
    public long LastOccurrenceAtTicks { get; set; }

    /// <summary>Número de ocorrências correspondentes agregadas neste Alerta.</summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>Momento do reconhecimento, quando aplicável (R3.3).</summary>
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Usuário que reconheceu o Alerta, quando aplicável.</summary>
    public Guid? AcknowledgedByUserId { get; set; }

    /// <summary>Momento da resolução, quando aplicável (R3.4).</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Usuário que resolveu manualmente o Alerta; nulo quando <see cref="AutoResolved"/>.</summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>Indica se a resolução foi automática pelo Motor_de_Alertas (R2.6).</summary>
    public bool AutoResolved { get; set; }

    /// <summary>Observação opcional registrada na resolução (R3.4).</summary>
    public string? ResolutionNote { get; set; }
}
