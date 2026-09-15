using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Implementação de <see cref="IAlertEmailSender"/> sobre <see cref="SmtpClient"/>
/// (biblioteca padrão do .NET — sem dependência de terceiros), usada quando
/// <c>Alerting:Smtp:Host</c> está configurado (R4). A composição da mensagem não
/// inclui credenciais nem dados sensíveis (R4.2/R4.5); falhas de envio nunca
/// vazam as credenciais SMTP no resumo do erro registrado.
/// </summary>
public sealed class SmtpAlertEmailSender : IAlertEmailSender
{
    private readonly AlertingOptions _options;
    private readonly ILogger<SmtpAlertEmailSender> _logger;

    public SmtpAlertEmailSender(IOptions<AlertingOptions> options, ILogger<SmtpAlertEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationSendResult> SendAsync(AlertEmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var smtp = _options.Smtp;

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
        };

        if (!string.IsNullOrWhiteSpace(smtp.User))
        {
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress),
            Subject = $"[EasyPanel] Alerta {message.Severity} — {message.RuleName}",
            Body = message.EventSummary,
        };

        foreach (var recipient in message.Recipients)
        {
            mail.To.Add(recipient);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
            await client.SendMailAsync(mail, timeoutCts.Token).ConfigureAwait(false);
            return NotificationSendResult.Ok();
        }
        catch (SmtpException ex)
        {
            _logger.LogWarning(ex, "Falha ao enviar e-mail de alerta {AlertId}.", message.AlertId);
            return NotificationSendResult.Fail($"Falha SMTP: {ex.StatusCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout ao enviar e-mail de alerta {AlertId}.", message.AlertId);
            return NotificationSendResult.Fail("Timeout ao enviar e-mail.");
        }
    }
}
