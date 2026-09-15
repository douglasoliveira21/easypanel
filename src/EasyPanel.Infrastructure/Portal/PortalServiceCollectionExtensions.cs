using EasyPanel.Modules.Portal;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Portal;

/// <summary>
/// Registro de DI dos serviços de domínio da Fase 10 (Portal do Cliente):
/// <see cref="IPortalFleetService"/>, <see cref="IPortalTicketService"/> e
/// <see cref="IPortalInvoiceService"/>, todos <c>scoped</c> (dependem do
/// <c>AppDbContext</c> e do <c>ICustomerContext</c>, ambos scoped). Pressupõe
/// que a persistência (<c>AddPersistence</c>) e o Tenancy (<c>AddTenancy</c>)
/// já estejam registrados.
/// </summary>
public static class PortalServiceCollectionExtensions
{
    /// <summary>Registra os serviços de leitura do Portal do Cliente.</summary>
    public static IServiceCollection AddPortalServices(this IServiceCollection services)
    {
        services.AddScoped<IPortalFleetService, PortalFleetService>();
        services.AddScoped<IPortalTicketService, PortalTicketService>();
        services.AddScoped<IPortalInvoiceService, PortalInvoiceService>();

        return services;
    }
}
