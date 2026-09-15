using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Período durante o qual Alertas que corresponderiam a uma <see cref="AlertRule"/>
/// e/ou a uma Impressora/Agente específico não geram notificação, embora continuem
/// sendo registrados normalmente (R7). Ao menos um de <see cref="AlertRuleId"/>,
/// <see cref="PrinterId"/> ou <see cref="WindowsClientId"/> é obrigatório.
/// </summary>
public class AlertSilence : TenantEntity
{
    /// <summary>Regra silenciada, quando o silenciamento é por regra.</summary>
    public Guid? AlertRuleId { get; set; }

    /// <summary>Impressora silenciada, quando o silenciamento é por impressora.</summary>
    public Guid? PrinterId { get; set; }

    /// <summary>Agente silenciado, quando o silenciamento é por agente.</summary>
    public Guid? WindowsClientId { get; set; }

    /// <summary>Início da vigência (UTC).</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Término programado da vigência (UTC).</summary>
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>Usuário que criou o silenciamento.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>Justificativa opcional.</summary>
    public string? Reason { get; set; }

    /// <summary>Momento do encerramento antecipado, quando aplicável (R7.5).</summary>
    public DateTimeOffset? EndedEarlyAt { get; set; }

    /// <summary>Usuário que encerrou antecipadamente, quando aplicável.</summary>
    public Guid? EndedEarlyByUserId { get; set; }
}
