using EasyPanel.Infrastructure.Alerting;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.UnitTests.Alerting;

/// <summary>
/// Testes do <see cref="LogOnlyAlertEmailSender"/> (Task 6.1 — R4.5): sempre
/// retorna sucesso e nunca lança, mesmo sem SMTP configurado.
/// </summary>
public sealed class LogOnlyAlertEmailSenderTests
{
    [Fact]
    public async Task SendAsync_AlwaysSucceeds()
    {
        var sender = new LogOnlyAlertEmailSender(NullLogger<LogOnlyAlertEmailSender>.Instance);

        var result = await sender.SendAsync(
            new AlertEmailMessage(["ops@example.com"], "Falha de coleta", "Critica", "Resumo do evento.", Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.Success);
    }
}
