using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Health;

/// <summary>
/// Health check do provedor de storage <c>Local</c> (R1.5): verifica que o
/// diretório base (<see cref="StorageOptions.LocalBasePath"/>) existe e é
/// gravável, escrevendo e removendo um arquivo de sondagem. Contraparte de
/// <see cref="StorageHealthCheck"/> (usado quando o provedor é <c>Minio</c>).
/// </summary>
public sealed class LocalStorageHealthCheck(IOptions<StorageOptions> options) : IHealthCheck
{
    private readonly StorageOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var basePath = _options.LocalBasePath;
            Directory.CreateDirectory(basePath);

            var probePath = Path.Combine(basePath, $".health-{Guid.NewGuid():N}");
            File.WriteAllBytes(probePath, [1]);
            File.Delete(probePath);

            return Task.FromResult(HealthCheckResult.Healthy("Storage (disco local) acessível."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Storage (disco local) inacessível.", ex));
        }
    }
}
