using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Registro somente-adição de uma mudança de <see cref="AlertState"/> de um
/// <see cref="Alert"/> (R3.5). Nenhuma entrada é atualizada ou removida.
/// </summary>
public class AlertTransition : TenantEntity
{
    /// <summary>Alerta ao qual esta transição pertence.</summary>
    public Guid AlertId { get; set; }

    /// <summary>Estado anterior.</summary>
    public AlertState FromState { get; set; }

    /// <summary>Novo estado.</summary>
    public AlertState ToState { get; set; }

    /// <summary>Usuário que executou a transição; nulo quando originada pelo sistema (Motor_de_Alertas).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Momento da transição (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Observação opcional (ex.: justificativa de resolução).</summary>
    public string? Note { get; set; }
}
