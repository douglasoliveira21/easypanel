using EasyPanel.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Registro de DI dos health checks de dependências obrigatórias (R1.3, R1.5).
///
/// Todos os checks de dependência são marcados com a tag <see cref="ReadyTag"/>
/// e são avaliados pelo endpoint de prontidão <c>/health/ready</c>. Se qualquer
/// dependência obrigatória (PostgreSQL, Redis ou S3/MinIO) estiver indisponível,
/// o endpoint reporta estado não saudável (HTTP 503). O endpoint de liveness
/// <c>/health/live</c> não avalia dependências.
/// </summary>
public static class HealthChecksServiceCollectionExtensions
{
    /// <summary>Tag aplicada aos checks avaliados pela prontidão (<c>/health/ready</c>).</summary>
    public const string ReadyTag = "ready";

    /// <summary>Nome do health check de PostgreSQL.</summary>
    public const string PostgresCheckName = "postgres";

    /// <summary>Nome do health check de Redis.</summary>
    public const string RedisCheckName = "redis";

    /// <summary>Nome do health check de storage (S3/MinIO).</summary>
    public const string StorageCheckName = "storage";

    /// <summary>
    /// Registra os health checks de PostgreSQL, Redis e storage (S3/MinIO),
    /// vinculando as opções de <see cref="RedisOptions"/> e
    /// <see cref="StorageOptions"/> a partir de <see cref="IConfiguration"/>.
    /// </summary>
    public static IServiceCollection AddDependencyHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.ConnectionString =
                        configuration.GetConnectionString("Redis") ?? string.Empty;
                }
            });

        services
            .AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName));

        // HttpClient nomeado usado pelo StorageHealthCheck (provedor Minio).
        services.AddHttpClient(StorageHealthCheck.HttpClientName);

        string[] readyTags = [ReadyTag];

        var healthChecksBuilder = services
            .AddHealthChecks()
            // PostgreSQL: usa o AppDbContext (Npgsql) para validar conectividade.
            .AddDbContextCheck<AppDbContext>(
                name: PostgresCheckName,
                tags: readyTags)
            // Redis: conexão + PING.
            .AddCheck<RedisHealthCheck>(
                name: RedisCheckName,
                tags: readyTags);

        // Storage: o provedor decide o check (Minio -> sondagem HTTP de
        // liveness; Local -> escrita/remoção de sondagem no diretório base).
        // Lido diretamente da configuração (não via DI) porque o provedor
        // precisa ser conhecido no momento do registro dos health checks.
        var storageProvider = configuration[$"{StorageOptions.SectionName}:{nameof(StorageOptions.Provider)}"];
        if (string.Equals(storageProvider, StorageOptions.ProviderLocal, StringComparison.OrdinalIgnoreCase))
        {
            healthChecksBuilder.AddCheck<LocalStorageHealthCheck>(name: StorageCheckName, tags: readyTags);
        }
        else
        {
            healthChecksBuilder.AddCheck<StorageHealthCheck>(name: StorageCheckName, tags: readyTags);
        }

        return services;
    }
}
