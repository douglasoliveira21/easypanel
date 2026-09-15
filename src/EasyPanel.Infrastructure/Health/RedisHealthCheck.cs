using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Health check da dependência obrigatória Redis (R1.5). Estabelece uma conexão
/// e executa um <c>PING</c>. Se a conexão não puder ser aberta (dependência
/// indisponível) ou não estiver configurada, reporta estado não saudável, o que
/// torna o endpoint <c>/health/ready</c> não saudável (HTTP 503).
/// </summary>
public sealed class RedisHealthCheck(IOptions<RedisOptions> options) : IHealthCheck
{
    private readonly RedisOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            return HealthCheckResult.Unhealthy("Redis não configurado (string de conexão ausente).");
        }

        var configuration = ConfigurationOptions.Parse(_options.ConnectionString);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = _options.TimeoutMilliseconds;
        configuration.ConnectRetry = 0;

        ConnectionMultiplexer? connection = null;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.TimeoutMilliseconds);

            connection = await ConnectionMultiplexer
                .ConnectAsync(configuration)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);

            if (!connection.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Não foi possível conectar ao Redis.");
            }

            var database = connection.GetDatabase();
            var latency = await database.PingAsync().ConfigureAwait(false);

            return HealthCheckResult.Healthy(
                $"Redis acessível (latência {latency.TotalMilliseconds:F0} ms).");
        }
        catch (Exception ex) when (ex is RedisConnectionException
            or OperationCanceledException
            or TimeoutException
            or RedisTimeoutException)
        {
            return HealthCheckResult.Unhealthy("Redis indisponível.", ex);
        }
        finally
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
