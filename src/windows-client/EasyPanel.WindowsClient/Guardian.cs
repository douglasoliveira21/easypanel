using Microsoft.Extensions.Logging;

namespace EasyPanel.WindowsClient;

/// <summary>
/// Supervisor interno do agente (R2.2): monitora os serviços registrados e
/// reinicia os que pararam de responder, registrando cada reinício. Uma única
/// varredura é exposta por <see cref="SuperviseOnceAsync"/> para permitir testes
/// determinísticos; o laço periódico é conduzido pelo host.
/// </summary>
public sealed class Guardian
{
    private readonly IReadOnlyList<ISupervisedService> _services;
    private readonly ILogger<Guardian> _logger;

    public Guardian(IEnumerable<ISupervisedService> services, ILogger<Guardian> logger)
    {
        _services = services.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Executa uma varredura de supervisão: reinicia serviços não saudáveis e
    /// retorna quantos foram reiniciados. Falhas de reinício são registradas e não
    /// interrompem a varredura dos demais serviços.
    /// </summary>
    public async Task<int> SuperviseOnceAsync(CancellationToken ct)
    {
        var restarted = 0;

        foreach (var service in _services)
        {
            ct.ThrowIfCancellationRequested();

            if (service.IsHealthy)
            {
                continue;
            }

            _logger.LogWarning("Serviço '{Service}' não saudável; reiniciando.", service.Name);

            try
            {
                await service.RestartAsync(ct).ConfigureAwait(false);
                restarted++;
                _logger.LogInformation("Serviço '{Service}' reiniciado pelo Guardian.", service.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao reiniciar o serviço '{Service}'.", service.Name);
            }
        }

        return restarted;
    }
}
