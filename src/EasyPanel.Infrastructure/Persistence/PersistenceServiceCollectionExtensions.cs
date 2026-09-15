using EasyPanel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Registro de DI da camada de persistência (EF Core + PostgreSQL).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="AppDbContext"/> sobre Npgsql, vincula as
    /// <see cref="DatabaseOptions"/> a partir de <see cref="IConfiguration"/>
    /// (variáveis de ambiente / arquivos por ambiente) e agenda o
    /// <see cref="MigrationHostedService"/> para aplicar migrações pendentes na
    /// inicialização (R1.4).
    ///
    /// A cadeia de conexão é resolvida, em ordem de precedência:
    /// <list type="number">
    ///   <item>Seção <c>Database:ConnectionString</c>.</item>
    ///   <item><c>ConnectionStrings:Default</c>.</item>
    /// </list>
    /// Segredos nunca são versionados (R1.6).
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.ConnectionString =
                        configuration.GetConnectionString("Default") ?? string.Empty;
                }
            });

        services.AddDbContext<AppDbContext>((serviceProvider, dbOptions) =>
        {
            var databaseOptions = serviceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>()
                .Value;

            dbOptions.UseNpgsql(
                databaseOptions.ConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsql.CommandTimeout(databaseOptions.MigrationLockTimeoutSeconds);
                });
        });

        services.AddHostedService<MigrationHostedService>();

        return services;
    }
}
