using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient;

/// <summary>
/// Laço de hospedagem do <see cref="Guardian"/> (R2.2): executa varreduras
/// periódicas de supervisão, reiniciando serviços internos parados. Uma falha na
/// varredura é registrada e não derruba o worker (auto-recovery — R2.3).
/// </summary>
public sealed class GuardianWorker : BackgroundService
{
    private readonly Guardian _guardian;
    private readonly AgentOptions _options;
    private readonly ILogger<GuardianWorker> _logger;

    public GuardianWorker(
        Guardian guardian,
        IOptions<AgentOptions> options,
        ILogger<GuardianWorker> logger)
    {
        _guardian = guardian;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.GuardianIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _guardian.SuperviseOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na varredura do Guardian.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
