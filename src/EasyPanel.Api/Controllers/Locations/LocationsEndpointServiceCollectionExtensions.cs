using EasyPanel.Infrastructure.Security;
using FluentValidation;

namespace EasyPanel.Api.Controllers.Locations;

/// <summary>
/// Composição de DI dos endpoints de cadastro de locais (tarefa 8.2 / R9).
/// Registra o serviço de domínio (<see cref="EasyPanel.Modules.Customers.ILocationService"/>)
/// e os validadores de contrato de entrada (R12.2).
/// </summary>
public static class LocationsEndpointServiceCollectionExtensions
{
    /// <summary>
    /// Registra as dependências dos endpoints de locais:
    /// <list type="bullet">
    ///   <item>o <see cref="EasyPanel.Modules.Customers.ILocationService"/> (via <c>AddLocationService</c>);</item>
    ///   <item>os validadores FluentValidation dos DTOs de entrada (R12.2).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddLocationsEndpoint(this IServiceCollection services)
    {
        services.AddLocationService();

        services.AddScoped<IValidator<CreateLocationApiRequest>, CreateLocationApiRequestValidator>();
        services.AddScoped<IValidator<UpdateLocationApiRequest>, UpdateLocationApiRequestValidator>();

        return services;
    }
}
