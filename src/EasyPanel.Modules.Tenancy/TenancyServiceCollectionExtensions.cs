using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Registro de DI do módulo de Tenancy.
/// </summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="ITenantContext"/> com tempo de vida <c>scoped</c>,
    /// de forma que cada requisição possua seu próprio contexto de tenant.
    ///
    /// A mesma instância de <see cref="TenantContext"/> é resolvida tanto por
    /// <see cref="ITenantContext"/> quanto por <see cref="TenantContext"/>
    /// concreto, permitindo que a camada de resolução (middleware) atribua o
    /// tenant e os consumidores o leiam pela abstração.
    /// </summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        // Fase 10 (Portal do Cliente): segundo nível de isolamento, mesmo padrão
        // scoped de TenantContext, resolvido pelo mesmo middleware.
        services.AddScoped<CustomerContext>();
        services.AddScoped<ICustomerContext>(sp => sp.GetRequiredService<CustomerContext>());

        return services;
    }
}
