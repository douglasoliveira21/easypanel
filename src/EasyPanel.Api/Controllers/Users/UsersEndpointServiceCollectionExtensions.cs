using EasyPanel.Api.Infrastructure;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Identity;
using FluentValidation;

namespace EasyPanel.Api.Controllers.Users;

/// <summary>
/// Composição de DI dos endpoints de gestão de usuários (tarefa 6.2 / R7).
/// Registra o serviço de domínio (<see cref="IUserService"/>), o acessor do
/// usuário atuante para enriquecer a auditoria (R7.8) e os validadores de contrato
/// (R12.2).
/// </summary>
public static class UsersEndpointServiceCollectionExtensions
{
    /// <summary>
    /// Registra as dependências dos endpoints de usuários:
    /// <list type="bullet">
    ///   <item>o <see cref="IUserService"/> (via <c>AddUserService</c>);</item>
    ///   <item>o <see cref="IHttpContextAccessor"/> e o
    ///     <see cref="ICurrentUserAccessor"/> baseado em <c>HttpContext</c>, para
    ///     resolver o ator das mutações a partir do claim <c>sub</c> (R6.3/R7.8);</item>
    ///   <item>os validadores FluentValidation dos DTOs de entrada (R12.2).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddUsersEndpoint(this IServiceCollection services)
    {
        // Serviço de domínio de gestão de usuários (R7).
        services.AddUserService();

        // Acessor do usuário atuante a partir do HttpContext (R6.3/R7.8).
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

        // Validadores de contrato de entrada (R12.2).
        services.AddScoped<IValidator<CreateUserApiRequest>, CreateUserApiRequestValidator>();
        services.AddScoped<IValidator<UpdateUserApiRequest>, UpdateUserApiRequestValidator>();

        return services;
    }
}
