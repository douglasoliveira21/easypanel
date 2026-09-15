using EasyPanel.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI da gestão de usuários (R7): vincula <see cref="IUserService"/>
/// ao <see cref="UserService"/> como <c>scoped</c> (depende do
/// <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/>, do
/// <c>AppDbContext</c>, do <see cref="EasyPanel.Modules.Tenancy.ITenantContext"/>,
/// do <see cref="ITokenService"/> e do
/// <see cref="EasyPanel.Modules.Auditing.IAuditLogger"/>, todos scoped).
///
/// Pressupõe que o Identity core (<c>AddPlatformIdentity</c>), o serviço de tokens
/// (<c>AddTokenService</c>) e a auditoria (<c>AddAuditing</c>) já estejam
/// registrados. Os endpoints/DTOs da API são registrados pela tarefa 6.2.
/// </summary>
public static class UserServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="IUserService"/> de gestão de usuários.</summary>
    public static IServiceCollection AddUserService(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}
