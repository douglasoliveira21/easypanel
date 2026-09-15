using EasyPanel.Modules.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Reporting;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 9 (Relatórios e
/// Dashboards): <see cref="IDashboardService"/>,
/// <see cref="IPrintConsumptionReportService"/>,
/// <see cref="IBillingReportService"/> e <see cref="ISlaReportService"/>,
/// todos <c>scoped</c> (dependem do <c>AppDbContext</c>, scoped).
/// Pressupõe que a persistência (<c>AddPersistence</c>) e os serviços de
/// Estoque (<c>AddInventoryServices</c>, Fase 5) já estejam registrados.
/// </summary>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>Registra os serviços de agregação do painel e dos relatórios.</summary>
    public static IServiceCollection AddReportingServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IPrintConsumptionReportService, PrintConsumptionReportService>();
        services.AddScoped<IBillingReportService, BillingReportService>();
        services.AddScoped<ISlaReportService, SlaReportService>();

        return services;
    }
}
