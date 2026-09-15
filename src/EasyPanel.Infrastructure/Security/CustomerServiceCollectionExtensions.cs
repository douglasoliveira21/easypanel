using EasyPanel.Modules.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI do cadastro de clientes (R8): vincula
/// <see cref="ICustomerService"/> ao <see cref="CustomerService"/> como
/// <c>scoped</c> (depende do <c>AppDbContext</c> e do
/// <see cref="EasyPanel.Modules.Tenancy.ITenantContext"/>, ambos scoped).
///
/// Pressupõe que a persistência (<c>AddPersistence</c>) e o contexto de tenant
/// (<c>AddTenancy</c>) já estejam registrados. Os endpoints/DTOs da API são
/// registrados pela tarefa 7.2.
/// </summary>
public static class CustomerServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="ICustomerService"/> do cadastro de clientes.</summary>
    public static IServiceCollection AddCustomerService(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        return services;
    }
}
