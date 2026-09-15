using System.Text.Json;
using EasyPanel.WindowsClient.Snmp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient;

/// <summary>Uma leitura de contador no corpo da submissão de coleta (espelha o contrato do backend — R12/R13).</summary>
internal sealed record CollectCounterBody(string CounterType, string? CounterTypeLabel, long Value);

/// <summary>Uma leitura de suprimento no corpo da submissão de coleta (espelha o contrato do backend — Fase 4/R2).</summary>
internal sealed record CollectSupplyBody(string Label, int Percent);

/// <summary>Corpo da submissão de coleta enviada ao backend (espelha <c>ClientCollectionRequest</c>).</summary>
internal sealed record CollectRequestBody(
    string IdempotencyKey,
    Guid? PrinterId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Success,
    string? Errors,
    IReadOnlyList<CollectCounterBody> Counters,
    string? RawStatus,
    IReadOnlyList<CollectSupplyBody> Supplies);

/// <summary>
/// <c>Net_Monitoring_Service</c> (Fase 4 — R1.1-R1.3/R1.5/R1.6): ciclo periódico
/// que consulta via SNMP cada impressora monitorável reportada pelo backend
/// (<see cref="IBackendClient.GetConfigAsync"/>), monta a submissão de coleta
/// (contadores + níveis de suprimento) e a enfileira na <see cref="ILocalQueue"/> —
/// nunca envia diretamente ao backend, unificando o caminho de envio com o
/// <see cref="CommunicationService"/>. Implementação de <see cref="ISupervisedService"/>:
/// supervisionado pelo <see cref="Guardian"/>, que também é quem efetivamente
/// inicia o laço interno (primeira varredura vê o serviço não saudável e chama
/// <see cref="RestartAsync"/>).
/// </summary>
public sealed class NetMonitoringService : ISupervisedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> NoIgnored = [];

    private readonly IBackendClient _backend;
    private readonly PrinterDiscoveryService _discovery;
    private readonly ILocalQueue _queue;
    private readonly AgentOptions _options;
    private readonly ILogger<NetMonitoringService> _logger;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private AgentConfig? _lastConfig;

    public NetMonitoringService(
        IBackendClient backend,
        PrinterDiscoveryService discovery,
        ILocalQueue queue,
        IOptions<AgentOptions> options,
        ILogger<NetMonitoringService> logger)
    {
        _backend = backend;
        _discovery = discovery;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Net_Monitoring_Service";

    /// <inheritdoc />
    public bool IsHealthy => _loopTask is { IsCompleted: false };

    /// <summary>Inicia (ou reinicia, se o laço anterior parou) o ciclo periódico de coleta.</summary>
    public Task RestartAsync(CancellationToken ct)
    {
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no ciclo do Net_Monitoring_Service.");
            }

            var interval = TimeSpan.FromSeconds(_lastConfig?.CollectionIntervalSeconds ?? 300);
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

    /// <summary>
    /// Executa um ciclo: obtém a configuração vigente (mantendo a última válida em
    /// falha — R1.2), sonda cada impressora monitorável e enfileira a submissão
    /// correspondente. Falha em uma impressora não interrompe as demais (R1.5).
    /// Retorna o número de submissões enfileiradas. Público para testes
    /// determinísticos.
    /// </summary>
    public async Task<int> RunCycleOnceAsync(CancellationToken ct)
    {
        var config = await _backend.GetConfigAsync(ct).ConfigureAwait(false);
        if (config is not null)
        {
            _lastConfig = config;
        }

        var effective = _lastConfig;
        if (effective is null || effective.MonitoredPrinters.Count == 0)
        {
            return 0;
        }

        var enqueued = 0;
        foreach (var printer in effective.MonitoredPrinters)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var candidates = await _discovery
                    .DiscoverAsync([printer.Ip], NoIgnored, SnmpCredentials.DefaultV2c, ct)
                    .ConfigureAwait(false);

                var candidate = candidates.FirstOrDefault();
                if (candidate is null)
                {
                    _logger.LogWarning("Impressora {PrinterId} ({Ip}) não respondeu à sonda SNMP.", printer.PrinterId, printer.Ip);
                    continue;
                }

                var reading = await _discovery.CollectAsync(candidate, SnmpCredentials.DefaultV2c, ct).ConfigureAwait(false);
                await EnqueueCollectionAsync(printer.PrinterId, reading, ct).ConfigureAwait(false);
                enqueued++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Falha ao coletar a impressora {PrinterId} ({Ip}).", printer.PrinterId, printer.Ip);
            }
        }

        return enqueued;
    }

    private async Task EnqueueCollectionAsync(Guid printerId, DeviceReading reading, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var idempotencyKey = Guid.NewGuid().ToString("N");

        var body = new CollectRequestBody(
            idempotencyKey,
            printerId,
            now,
            now,
            Success: true,
            Errors: null,
            reading.Counters.Select(ToCounterBody).ToList(),
            RawStatus: reading.Status.ToString(),
            reading.SupplyLevels.Select(kv => new CollectSupplyBody(kv.Key, kv.Value)).ToList());

        var payload = JsonSerializer.Serialize(body, JsonOptions);
        await _queue.EnqueueAsync(new QueuedCollection(idempotencyKey, payload, now), ct).ConfigureAwait(false);
    }

    private static CollectCounterBody ToCounterBody(CounterReading counter) => counter.Kind switch
    {
        CounterKind.BlackAndWhite => new CollectCounterBody("BlackAndWhite", null, counter.Value),
        CounterKind.Color => new CollectCounterBody("Color", null, counter.Value),
        CounterKind.Scan => new CollectCounterBody("Scan", null, counter.Value),
        _ => new CollectCounterBody("Other", counter.Label ?? counter.Kind.ToString(), counter.Value),
    };
}
