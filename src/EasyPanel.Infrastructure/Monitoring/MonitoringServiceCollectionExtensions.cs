using EasyPanel.Modules.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Registro de DI dos serviços de monitoramento da Fase 2 que não pertencem à
/// autenticação do agente: opções (<see cref="MonitoringOptions"/>), o
/// <see cref="TimeProvider"/> e o worker <see cref="HeartbeatMonitor"/> (R6.4).
/// </summary>
public static class MonitoringServiceCollectionExtensions
{
    /// <summary>
    /// Vincula/valida as <see cref="MonitoringOptions"/> e registra o
    /// <see cref="HeartbeatMonitor"/> como serviço hospedado.
    /// </summary>
    public static IServiceCollection AddMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // TimeProvider injetável (idempotente caso já registrado por outro módulo).
        services.TryAddSingleton(TimeProvider.System);

        // Processador de coletas e worker de drenagem da fila (R14.2/R14.3). O
        // processador só depende de serviços singleton (ISystemDbContextFactory,
        // ILogger), portanto pode ser singleton e injetado no worker hospedado.
        services.AddSingleton<ICollectionProcessor, CollectionProcessor>();

        services.AddHostedService<HeartbeatMonitor>();
        services.AddHostedService<CollectionProcessingWorker>();

        return services;
    }
}
