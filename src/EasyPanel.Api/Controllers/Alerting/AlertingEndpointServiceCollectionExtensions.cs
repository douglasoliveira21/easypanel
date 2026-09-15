using EasyPanel.Infrastructure.Alerting;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>
/// Composição de DI dos endpoints de Alertas e Notificações (Fase 3 — R1/R3/R7).
/// Registra os serviços de domínio (via <c>AddAlertingServices</c>); os endpoints
/// não usam validadores FluentValidation dedicados — a validação de entrada é
/// feita pelos serviços de domínio (Result → 400), no mesmo padrão de
/// <c>PrintersController</c>/<c>CountersController</c> da Fase 2.
/// </summary>
public static class AlertingEndpointServiceCollectionExtensions
{
    /// <summary>Registra as dependências dos endpoints de Alertas e Notificações.</summary>
    public static IServiceCollection AddAlertingEndpoint(this IServiceCollection services)
    {
        services.AddAlertingServices();
        return services;
    }
}
