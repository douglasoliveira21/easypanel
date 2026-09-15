using Microsoft.Extensions.Logging;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Implementação de <see cref="IAlertEmailSender"/> usada quando nenhum servidor
/// SMTP está configurado (<c>Alerting:Smtp:Host</c> vazio — padrão em
/// desenvolvimento/testes). Registra apenas o fato do envio, sem credenciais nem
/// corpo completo (R4.5), no mesmo padrão de <c>LogOnlyPasswordResetNotifier</c>
/// da Fase 1.
/// </summary>
public sealed class LogOnlyAlertEmailSender : IAlertEmailSender
{
    private readonly ILogger<LogOnlyAlertEmailSender> _logger;

    public LogOnlyAlertEmailSender(ILogger<LogOnlyAlertEmailSender> logger) => _logger = logger;

    /// <inheritdoc />
    public Task<NotificationSendResult> SendAsync(AlertEmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        _logger.LogInformation(
            "E-mail de alerta {AlertId} (severidade {Severity}) seria enviado a {RecipientCount} destinatário(s) — SMTP não configurado (log-only).",
            message.AlertId,
            message.Severity,
            message.Recipients.Count);

        return Task.FromResult(NotificationSendResult.Ok());
    }
}
