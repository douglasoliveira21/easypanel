using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Implementação de <see cref="IAlertWebhookSender"/> via <see cref="HttpClient"/>
/// nomeado (R5). Assina o corpo com HMAC-SHA256 usando o segredo compartilhado da
/// <c>AlertRule</c> (R5.2) e nunca inclui o segredo no corpo, em logs ou em
/// exceções (R5.6). Sucesso = resposta HTTP 2xx.
/// </summary>
public sealed class HttpAlertWebhookSender : IAlertWebhookSender
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpAlertWebhookSender> _logger;

    public HttpAlertWebhookSender(HttpClient httpClient, ILogger<HttpAlertWebhookSender> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationSendResult> SendAsync(AlertWebhookMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.Serialize(new
        {
            alertId = message.AlertId,
            tenantId = message.TenantId,
            ruleId = message.AlertRuleId,
            severity = message.Severity,
            printerId = message.PrinterId,
            windowsClientId = message.WindowsClientId,
            occurredAt = message.OccurredAt,
            detail = message.EventSummary,
        });

        var signature = ComputeSignature(payload, message.Secret);

        using var request = new HttpRequestMessage(HttpMethod.Post, message.Url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-EasyPanel-Signature", $"sha256={signature}");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return NotificationSendResult.Ok((int)response.StatusCode);
            }

            return NotificationSendResult.Fail(
                $"Resposta HTTP não-2xx: {(int)response.StatusCode}", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha ao despachar webhook do alerta {AlertId}.", message.AlertId);
            return NotificationSendResult.Fail("Falha de conexão com o webhook.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Timeout ao despachar webhook do alerta {AlertId}.", message.AlertId);
            return NotificationSendResult.Fail("Timeout ao despachar webhook.");
        }
    }

    private static string ComputeSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexStringLower(hash);
    }
}
