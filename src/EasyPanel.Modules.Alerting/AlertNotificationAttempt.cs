using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Registro somente-adição de uma tentativa de envio de notificação de um
/// <see cref="Alert"/> por um <see cref="NotificationChannel"/> (R6.1). Histórico
/// completo para diagnóstico de falhas de entrega (R6).
/// </summary>
public class AlertNotificationAttempt : TenantEntity
{
    /// <summary>Alerta ao qual esta tentativa se refere.</summary>
    public Guid AlertId { get; set; }

    /// <summary>Canal usado nesta tentativa.</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Resultado da tentativa.</summary>
    public NotificationOutcome Outcome { get; set; }

    /// <summary>Número sequencial da tentativa para o item de outbox correspondente.</summary>
    public int AttemptNumber { get; set; }

    /// <summary>Código de status HTTP retornado (somente canal webhook), quando aplicável.</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>
    /// Resumo do erro, sem dados sensíveis (nunca inclui <see cref="AlertRule.WebhookSecret"/>
    /// ou credenciais de e-mail).
    /// </summary>
    public string? ErrorSummary { get; set; }

    /// <summary>Momento da tentativa (UTC).</summary>
    public DateTimeOffset AttemptedAt { get; set; }

    /// <summary>Ticks (UTC) de <see cref="AttemptedAt"/>. Usado como cursor de listagem.</summary>
    public long AttemptedAtTicks { get; set; }
}
