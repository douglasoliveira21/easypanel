using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IClientUpdateService"/> (R16.1).
///
/// Retorna os metadados da versão de atualização disponível (versão, URL no
/// armazenamento, hash e assinatura) a partir das <see cref="MonitoringOptions"/>.
/// Quando não há versão publicada (<see cref="ClientUpdateOptions.Version"/> vazio),
/// responde <see cref="MonitoringErrors.NotFound"/> (→ 404), indicando ausência de
/// atualização.
/// </summary>
public sealed class ClientUpdateService : IClientUpdateService
{
    private readonly IClientContext _clientContext;
    private readonly MonitoringOptions _options;

    public ClientUpdateService(IClientContext clientContext, IOptions<MonitoringOptions> options)
    {
        _clientContext = clientContext;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<Result<ClientUpdateInfo>> GetLatestAsync(CancellationToken ct)
    {
        if (!_clientContext.IsAuthenticated)
        {
            return Task.FromResult(Result.Failure<ClientUpdateInfo>(MonitoringErrors.Unauthorized));
        }

        var update = _options.Update;
        if (string.IsNullOrWhiteSpace(update.Version))
        {
            return Task.FromResult(Result.Failure<ClientUpdateInfo>(MonitoringErrors.NotFound));
        }

        var info = new ClientUpdateInfo(
            update.Version,
            update.PackageUrl,
            update.Sha256,
            update.Signature);

        return Task.FromResult(Result.Success(info));
    }
}
