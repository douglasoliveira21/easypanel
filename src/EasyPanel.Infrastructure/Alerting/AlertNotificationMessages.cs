namespace EasyPanel.Infrastructure.Alerting;

/// <summary>Resultado do envio de uma notificação por um canal (R4.3/R5.4).</summary>
public sealed record NotificationSendResult(bool Success, int? HttpStatusCode, string? ErrorSummary)
{
    /// <summary>Cria um resultado de sucesso.</summary>
    public static NotificationSendResult Ok(int? httpStatusCode = null) => new(true, httpStatusCode, null);

    /// <summary>Cria um resultado de falha com um resumo do erro, sem dados sensíveis.</summary>
    public static NotificationSendResult Fail(string errorSummary, int? httpStatusCode = null) =>
        new(false, httpStatusCode, errorSummary);
}

/// <summary>Mensagem de notificação de Alerta a compor e enviar por e-mail (R4.2).</summary>
public sealed record AlertEmailMessage(
    IReadOnlyList<string> Recipients,
    string RuleName,
    string Severity,
    string EventSummary,
    Guid AlertId);

/// <summary>Mensagem de notificação de Alerta a enviar por webhook assinado (R5.2).</summary>
public sealed record AlertWebhookMessage(
    string Url,
    string Secret,
    Guid AlertId,
    Guid TenantId,
    Guid AlertRuleId,
    string Severity,
    Guid? PrinterId,
    Guid? WindowsClientId,
    DateTimeOffset OccurredAt,
    string EventSummary);

/// <summary>Envia notificações de Alerta por e-mail (R4).</summary>
public interface IAlertEmailSender
{
    /// <summary>Envia a mensagem, sem incluir credenciais ou dados sensíveis (R4.2/R4.5).</summary>
    Task<NotificationSendResult> SendAsync(AlertEmailMessage message, CancellationToken ct);
}

/// <summary>Envia notificações de Alerta por webhook de saída assinado (R5).</summary>
public interface IAlertWebhookSender
{
    /// <summary>Envia a requisição HTTPS assinada por HMAC (R5.2), sem logar o segredo (R5.6).</summary>
    Task<NotificationSendResult> SendAsync(AlertWebhookMessage message, CancellationToken ct);
}
