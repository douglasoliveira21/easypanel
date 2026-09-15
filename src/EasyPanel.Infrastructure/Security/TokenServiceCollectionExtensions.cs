using EasyPanel.Modules.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI do serviço de tokens (R2). Vincula <see cref="JwtOptions"/> a
/// partir da seção <c>Jwt</c> da configuração, valida-as na inicialização
/// (DataAnnotations, incluindo o teto de 15 min do access token — R2.6) e
/// registra <see cref="ITokenService"/> como <c>scoped</c> (usa o
/// <c>AppDbContext</c> scoped).
///
/// Esta tarefa (3.2) registra apenas as opções e o serviço; o esquema de
/// autenticação/validação de bearer (<c>AddJwtBearer</c>) e os endpoints de login
/// pertencem à tarefa 3.3.
/// </summary>
public static class TokenServiceCollectionExtensions
{
    /// <summary>
    /// Registra as <see cref="JwtOptions"/> e o <see cref="ITokenService"/>.
    /// </summary>
    public static IServiceCollection AddTokenService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ITokenService, TokenService>();

        return services;
    }
}
