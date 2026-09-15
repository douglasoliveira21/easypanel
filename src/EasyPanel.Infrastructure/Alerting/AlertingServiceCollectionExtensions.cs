using EasyPanel.Modules.Alerting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 3 (Alertas e Notificações):
/// <see cref="IAlertRuleService"/>, <see cref="IAlertSilenceService"/> e
/// <see cref="IAlertService"/>, todos <c>scoped</c> (dependem do
/// <c>AppDbContext</c> e do <see cref="EasyPanel.Modules.Identity.ICurrentUserAccessor"/>,
/// ambos scoped). Pressupõe que a persistência (<c>AddPersistence</c>) já esteja
/// registrada.
/// </summary>
public static class AlertingServiceCollectionExtensions
{
    /// <summary>Registra os serviços de domínio de regra/alerta/silenciamento.</summary>
    public static IServiceCollection AddAlertingServices(this IServiceCollection services)
    {
        // TimeProvider injetável (idempotente caso já registrado por outro módulo).
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAlertRuleService, AlertRuleService>();
        services.AddScoped<IAlertSilenceService, AlertSilenceService>();
        services.AddScoped<IAlertService, AlertService>();

        return services;
    }

    /// <summary>
    /// Vincula/valida as <see cref="AlertingOptions"/> e registra o motor de
    /// avaliação (<see cref="AlertEngine"/>), os canais de notificação
    /// (<see cref="IAlertEmailSender"/>/<see cref="IAlertWebhookSender"/>) e o
    /// despachante (<see cref="AlertNotificationDispatcher"/>) como serviços
    /// hospedados.
    /// </summary>
    public static IServiceCollection AddAlertingEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AlertingOptions>()
            .Bind(configuration.GetSection(AlertingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        // Canal de e-mail (R4): SMTP real quando configurado, senão log-only
        // (mesmo padrão do IPasswordResetNotifier da Fase 1).
        services.AddSingleton<IAlertEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AlertingOptions>>().Value;
            return string.IsNullOrWhiteSpace(options.Smtp.Host)
                ? new LogOnlyAlertEmailSender(sp.GetRequiredService<ILogger<LogOnlyAlertEmailSender>>())
                : new SmtpAlertEmailSender(
                    sp.GetRequiredService<IOptions<AlertingOptions>>(),
                    sp.GetRequiredService<ILogger<SmtpAlertEmailSender>>());
        });

        // Canal de webhook (R5): HttpClient nomeado via typed client.
        services.AddHttpClient<IAlertWebhookSender, HttpAlertWebhookSender>();

        services.AddHostedService<AlertEngine>();
        services.AddHostedService<AlertNotificationDispatcher>();

        return services;
    }
}
