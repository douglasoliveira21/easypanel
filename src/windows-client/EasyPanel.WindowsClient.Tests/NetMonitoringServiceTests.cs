using EasyPanel.WindowsClient.Snmp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes do <see cref="NetMonitoringService"/> (Task 7.1 — R1.1/R1.3/R1.5/R1.6,
/// R2.1): ciclo com uma impressora que falha e outra que responde enfileira
/// apenas a que respondeu; o payload enfileirado inclui contadores e suprimentos
/// sem segredo SNMP; falha em obter a configuração mantém a anterior válida.
/// </summary>
public sealed class NetMonitoringServiceTests
{
    private static readonly Guid PrinterOk = Guid.NewGuid();
    private static readonly Guid PrinterDown = Guid.NewGuid();

    [Fact]
    public async Task RunCycleOnce_OnePrinterFails_OtherSucceeds_EnqueuesOnlyTheSuccess()
    {
        var collector = new FakeCollector();
        collector.Probes["10.0.0.1"] = new DeviceProbe("HP LaserJet", null, null);
        collector.Values["10.0.0.1"] = new Dictionary<string, string>
        {
            [StandardOids.SerialNumber] = "SN1",
            [StandardOids.DeviceStatus] = "2",
        };
        // 10.0.0.2 não tem probe cadastrada: a sonda falha (impressora indisponível).

        var backend = new FakeBackendClient(new AgentConfig(
            300,
            [],
            [],
            [
                new AgentMonitoredPrinter(PrinterOk, "10.0.0.1", null, null, "HP"),
                new AgentMonitoredPrinter(PrinterDown, "10.0.0.2", null, null, null),
            ]));

        var queue = new FakeLocalQueue();
        var service = CreateService(backend, collector, queue);

        var enqueued = await service.RunCycleOnceAsync(CancellationToken.None);

        Assert.Equal(1, enqueued);
        Assert.Single(queue.Items);
    }

    [Fact]
    public async Task RunCycleOnce_Payload_IncludesCountersAndSupplies_WithoutSnmpSecret()
    {
        var collector = new FakeCollector();
        collector.Probes["10.0.0.1"] = new DeviceProbe("HP LaserJet", null, null);
        collector.Values["10.0.0.1"] = new Dictionary<string, string>
        {
            [StandardOids.SerialNumber] = "SN1",
            [StandardOids.DeviceStatus] = "2",
            ["1.3.6.1.2.1.43.10.2.1.4.1.1"] = "700",
            [StandardOids.SupplyDescriptionPrefix + "1"] = "Black Toner",
            [StandardOids.SupplyLevelPrefix + "1"] = "20",
            [StandardOids.SupplyMaxCapacityPrefix + "1"] = "100",
        };

        var backend = new FakeBackendClient(new AgentConfig(
            300, [], [], [new AgentMonitoredPrinter(PrinterOk, "10.0.0.1", null, null, "HP")]));

        var queue = new FakeLocalQueue();
        var service = CreateService(backend, collector, queue);

        await service.RunCycleOnceAsync(CancellationToken.None);

        var payload = Assert.Single(queue.Items).Payload;
        Assert.Contains("\"printerId\"", payload);
        Assert.Contains("700", payload);
        Assert.Contains("Black Toner", payload);
        Assert.Contains("\"percent\":20", payload);
        Assert.DoesNotContain("public", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("community", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunCycleOnce_ConfigFetchFails_KeepsLastKnownConfig()
    {
        var collector = new FakeCollector();
        collector.Probes["10.0.0.1"] = new DeviceProbe("HP LaserJet", null, null);

        var backend = new FakeBackendClient(new AgentConfig(
            300, [], [], [new AgentMonitoredPrinter(PrinterOk, "10.0.0.1", null, null, "HP")]));

        var queue = new FakeLocalQueue();
        var service = CreateService(backend, collector, queue);

        var first = await service.RunCycleOnceAsync(CancellationToken.None);
        Assert.Equal(1, first);

        // Próxima obtenção de config falha (retorna null); o ciclo deve continuar
        // usando a última configuração válida conhecida (R1.2/R15.4).
        backend.NextConfig = null;
        var second = await service.RunCycleOnceAsync(CancellationToken.None);

        Assert.Equal(1, second);
        Assert.Equal(2, queue.Items.Count);
    }

    private static NetMonitoringService CreateService(FakeBackendClient backend, FakeCollector collector, FakeLocalQueue queue) =>
        new(
            backend,
            new PrinterDiscoveryService(collector, new PrinterDriverSelector()),
            queue,
            Options.Create(new AgentOptions()),
            NullLogger<NetMonitoringService>.Instance);

    private sealed class FakeBackendClient(AgentConfig? initialConfig) : IBackendClient
    {
        public AgentConfig? NextConfig { get; set; } = initialConfig;

        public Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<AgentConfig?> GetConfigAsync(CancellationToken ct) => Task.FromResult(NextConfig);
    }

    private sealed class FakeLocalQueue : ILocalQueue
    {
        public List<QueuedCollection> Items { get; } = [];

        public Task EnqueueAsync(QueuedCollection item, CancellationToken ct)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QueuedCollection>> PeekAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<QueuedCollection>>(Items.Take(max).ToList());

        public Task AcknowledgeAsync(string idempotencyKey, CancellationToken ct)
        {
            Items.RemoveAll(i => i.IdempotencyKey == idempotencyKey);
            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(Items.Count);
    }

    private sealed class FakeCollector : ISnmpCollector
    {
        public Dictionary<string, DeviceProbe> Probes { get; } = [];

        public Dictionary<string, IReadOnlyDictionary<string, string>> Values { get; } = [];

        public Task<DeviceProbe> ProbeAsync(string host, SnmpCredentials credentials, CancellationToken ct)
        {
            if (Probes.TryGetValue(host, out var probe))
            {
                return Task.FromResult(probe);
            }

            throw new TimeoutException($"sem resposta de {host}");
        }

        public Task<SnmpResult> QueryAsync(
            string host,
            SnmpCredentials credentials,
            IReadOnlyList<string> oids,
            CancellationToken ct)
        {
            var values = Values.TryGetValue(host, out var v) ? v : new Dictionary<string, string>();
            return Task.FromResult(new SnmpResult(values));
        }
    }
}
