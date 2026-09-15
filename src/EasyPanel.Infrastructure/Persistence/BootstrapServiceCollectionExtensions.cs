using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Registro de DI do bootstrap de inicialização (tarefa 10.1): vincula as
/// <see cref="BootstrapOptions"/> a partir da configuração e agenda o
/// <see cref="BootstrapHostedService"/>, que semeia papéis e (opcionalmente) o
/// Super Admin inicial após a aplicação das migrações.
///
/// <para>Deve ser chamado após <c>AddPersistence</c> (que registra o
/// <see cref="MigrationHostedService"/>) e <c>AddPlatformIdentity</c> (que registra
/// o <see cref="IRoleSeeder"/> e o Identity). Como os <c>IHostedService</c> são
/// executados na ordem de registro, o bootstrap roda depois das migrações.</para>
/// </summary>
public static class BootstrapServiceCollectionExtensions
{
    /// <summary>Registra as opções e o serviço hospedado de bootstrap.</summary>
    public static IServiceCollection AddBootstrap(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection(BootstrapOptions.SectionName));

        services.AddHostedService<BootstrapHostedService>();

        return services;
    }
}
