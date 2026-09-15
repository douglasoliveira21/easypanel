using EasyPanel.Infrastructure.Alerting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyPanel.UnitTests.Alerting;

/// <summary>
/// Testes de composição de DI da Fase 3 (Task 6.1 — R4): confirma a seleção
/// condicional do <see cref="IAlertEmailSender"/> por configuração — SMTP real
/// quando <c>Alerting:Smtp:Host</c> está preenchido, log-only quando vazio.
/// </summary>
public sealed class AlertingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAlertingEngine_WithoutSmtpHost_ResolvesLogOnlySender()
    {
        var provider = BuildProvider(smtpHost: null);

        var sender = provider.GetRequiredService<IAlertEmailSender>();

        Assert.IsType<LogOnlyAlertEmailSender>(sender);
    }

    [Fact]
    public void AddAlertingEngine_WithSmtpHost_ResolvesSmtpSender()
    {
        var provider = BuildProvider(smtpHost: "smtp.example.com");

        var sender = provider.GetRequiredService<IAlertEmailSender>();

        Assert.IsType<SmtpAlertEmailSender>(sender);
    }

    private static ServiceProvider BuildProvider(string? smtpHost)
    {
        var configValues = new Dictionary<string, string?>();
        if (smtpHost is not null)
        {
            configValues["Alerting:Smtp:Host"] = smtpHost;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAlertingEngine(configuration);

        return services.BuildServiceProvider();
    }
}
