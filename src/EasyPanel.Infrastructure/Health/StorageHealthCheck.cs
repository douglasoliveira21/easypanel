using System.Net.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Health check da dependência obrigatória de armazenamento compatível com S3
/// (MinIO) (R1.5). Faz uma sondagem HTTP ao endpoint de liveness do serviço
/// (<c>/minio/health/live</c> por padrão). Se o endpoint estiver inacessível ou
/// não estiver configurado, reporta estado não saudável, tornando
/// <c>/health/ready</c> não saudável (HTTP 503).
/// </summary>
public sealed class StorageHealthCheck(
    IHttpClientFactory httpClientFactory,
    IOptions<StorageOptions> options) : IHealthCheck
{
    /// <summary>Nome do <see cref="HttpClient"/> nomeado usado por este check.</summary>
    public const string HttpClientName = "storage-health";

    private readonly StorageOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return HealthCheckResult.Unhealthy("Storage (S3/MinIO) não configurado (endpoint ausente).");
        }

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var baseUri))
        {
            return HealthCheckResult.Unhealthy(
                $"Endpoint de storage inválido: '{_options.Endpoint}'.");
        }

        if (!Uri.TryCreate(baseUri, _options.HealthPath, out var probeUri))
        {
            return HealthCheckResult.Unhealthy(
                $"Caminho de health de storage inválido: '{_options.HealthPath}'.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.TimeoutMilliseconds);

            using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Storage (S3/MinIO) acessível.")
                : HealthCheckResult.Unhealthy(
                    $"Storage (S3/MinIO) respondeu com status {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException
            or OperationCanceledException
            or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Storage (S3/MinIO) indisponível.", ex);
        }
    }
}
