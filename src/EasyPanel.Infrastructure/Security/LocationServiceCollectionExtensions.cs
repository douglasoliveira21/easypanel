using EasyPanel.Modules.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI do cadastro de locais (R9): vincula
/// <see cref="ILocationService"/> ao <see cref="LocationService"/> como
/// <c>scoped</c> (depende do <c>AppDbContext</c> e do
/// <see cref="EasyPanel.Modules.Tenancy.ITenantContext"/>, ambos scoped).
///
/// Pressupõe que a persistência (<c>AddPersistence</c>) e o contexto de tenant
/// (<c>AddTenancy</c>) já estejam registrados. Os endpoints/DTOs da API são
/// registrados pela tarefa 8.2.
/// </summary>
public static class LocationServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="ILocationService"/> do cadastro de locais.</summary>
    public static IServiceCollection AddLocationService(this IServiceCollection services)
    {
        services.AddScoped<ILocationService, LocationService>();
        return services;
    }
}
