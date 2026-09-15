using EasyPanel.Infrastructure.Security;
using FluentValidation;

namespace EasyPanel.Api.Controllers.Customers;

/// <summary>
/// Composição de DI dos endpoints de cadastro de clientes (tarefa 7.2 / R8).
/// Registra o serviço de domínio (<see cref="EasyPanel.Modules.Customers.ICustomerService"/>)
/// e os validadores de contrato de entrada (R12.2).
/// </summary>
public static class CustomersEndpointServiceCollectionExtensions
{
    /// <summary>
    /// Registra as dependências dos endpoints de clientes:
    /// <list type="bullet">
    ///   <item>o <see cref="EasyPanel.Modules.Customers.ICustomerService"/> (via <c>AddCustomerService</c>);</item>
    ///   <item>os validadores FluentValidation dos DTOs de entrada (R12.2).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCustomersEndpoint(this IServiceCollection services)
    {
        services.AddCustomerService();

        services.AddScoped<IValidator<CreateCustomerApiRequest>, CreateCustomerApiRequestValidator>();
        services.AddScoped<IValidator<UpdateCustomerApiRequest>, UpdateCustomerApiRequestValidator>();

        return services;
    }
}
