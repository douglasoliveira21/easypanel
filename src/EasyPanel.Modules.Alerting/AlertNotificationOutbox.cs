using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Item pendente de despacho de notificação de um <see cref="Alert"/>, criado pelo
/// Motor_de_Alertas somente na abertura de um novo Alerta (não a cada ocorrência
/// subsequente — evita spam de notificação). Drenado pelo
/// <c>AlertNotificationDispatcher</c> (Infrastructure), que aplica retry com
/// backoff exponencial e verifica <see cref="AlertSilence"/> vigente no momento do
/// envio (R7.3).
/// </summary>
public class AlertNotificationOutbox : TenantEntity
{
    /// <summary>Alerta ao qual esta notificação pertence.</summary>
    public Guid AlertId { get; set; }

    /// <summary>Canal de despacho.</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Estado atual do item.</summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Número de tentativas de despacho já realizadas.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Próximo momento elegível para nova tentativa (UTC).</summary>
    public DateTimeOffset NextAttemptAt { get; set; }
}
