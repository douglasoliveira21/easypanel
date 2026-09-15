using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient;

/// <summary>
/// <c>Communication_Service</c> (Fase 4 — R1.4): envolve o <see cref="QueueResender"/>
/// já existente (Fase 2, não alterado) num laço periódico próprio. Implementação
/// de <see cref="ISupervisedService"/>: supervisionado pelo <see cref="Guardian"/>,
/// que também é quem efetivamente inicia o laço interno (primeira varredura vê o
/// serviço não saudável e chama <see cref="RestartAsync"/>).
/// </summary>
public sealed class CommunicationService : ISupervisedService
{
    private readonly QueueResender _resender;
    private readonly AgentOptions _options;
    private readonly ILogger<CommunicationService> _logger;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public CommunicationService(QueueResender resender, IOptions<AgentOptions> options, ILogger<CommunicationService> logger)
    {
        _resender = resender;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Communication_Service";

    /// <inheritdoc />
    public bool IsHealthy => _loopTask is { IsCompleted: false };

    /// <summary>Inicia (ou reinicia, se o laço anterior parou) a drenagem periódica da fila local.</summary>
    public Task RestartAsync(CancellationToken ct)
    {
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.ResendIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _resender.ResendBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na drenagem da fila local pelo Communication_Service.");
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
