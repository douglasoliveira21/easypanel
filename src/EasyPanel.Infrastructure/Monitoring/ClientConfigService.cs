using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IClientConfigService"/> (R15.1/R15.2).
///
/// Retorna a configuração vigente do agente autenticado, resolvida da identidade
/// do agente (<see cref="IClientContext"/>), nunca da requisição. O intervalo de
/// coleta padrão vem das <see cref="MonitoringOptions"/>; alvos de descoberta e
/// impressoras ignoradas ficam vazios até haver configuração por agente (fase
/// futura). <see cref="ClientConfig.MonitoredPrinters"/> (Fase 4) lista as
/// impressoras do Local do agente com monitoramento habilitado — é o que o
/// <c>Net_Monitoring_Service</c> do agente usa para saber o que consultar via SNMP
/// e sob qual <c>PrinterId</c> reportar.
/// </summary>
public sealed class ClientConfigService : IClientConfigService
{
    private readonly IClientContext _clientContext;
    private readonly MonitoringOptions _options;
    private readonly AppDbContext _context;

    public ClientConfigService(IClientContext clientContext, IOptions<MonitoringOptions> options, AppDbContext context)
    {
        _clientContext = clientContext;
        _options = options.Value;
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<ClientConfig>> GetAsync(CancellationToken ct)
    {
        if (!_clientContext.IsAuthenticated || _clientContext.LocationId is not { } locationId)
        {
            return Result.Failure<ClientConfig>(MonitoringErrors.Unauthorized);
        }

        // Filtro global já escopa ao tenant do agente; restringe ainda ao Local.
        var monitoredPrinters = await _context.Set<Printer>()
            .AsNoTracking()
            .Where(p => p.LocationId == locationId
                && p.MonitoringEnabled
                && p.Status != PrinterStatus.Disabled
                && p.Ip != null)
            .Select(p => new MonitoredPrinter(p.Id, p.Ip!, p.Protocolo, p.Porta, p.Fabricante))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var config = new ClientConfig(
            _options.DefaultCollectionIntervalSeconds,
            DiscoveryTargets: [],
            IgnoredPrinters: [],
            MonitoredPrinters: monitoredPrinters);

        return Result.Success(config);
    }
}
