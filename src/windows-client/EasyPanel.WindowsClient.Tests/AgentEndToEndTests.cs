using EasyPanel.WindowsClient.Snmp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Teste de ponta a ponta do agente (Task 8.1 — R1.1-R1.6): um ciclo do
/// <see cref="NetMonitoringService"/> (SNMP → fila local) seguido de um ciclo do
/// <see cref="CommunicationService"/> (fila local → backend) resulta numa
/// submissão de coleta "recebida" pelo backend com contadores e níveis de
/// suprimento, sem duplicação.
/// </summary>
public sealed class AgentEndToEndTests
{
    [Fact]
    public async Task FullCycle_CollectsAndDrains_SubmissionReachesBackend_WithCountersAndSupplies()
    {
        var printerId = Guid.NewGuid();
        var collector = new FakeCollector();
        collector.Probes["10.0.0.9"] = new DeviceProbe("HP LaserJet", null, null);
        collector.Values["10.0.0.9"] = new Dictionary<string, string>
        {
            [StandardOids.SerialNumber] = "SN-E2E",
            [StandardOids.DeviceStatus] = "2",
            ["1.3.6.1.2.1.43.10.2.1.4.1.1"] = "1234",
            [StandardOids.SupplyDescriptionPrefix + "1"] = "Black Toner",
            [StandardOids.SupplyLevelPrefix + "1"] = "8",
            [StandardOids.SupplyMaxCapacityPrefix + "1"] = "100",
        };

        var backend = new RecordingBackend(new AgentConfig(
            300, [], [], [new AgentMonitoredPrinter(printerId, "10.0.0.9", null, null, "HP")]));

        using var queue = new SqliteLocalQueue($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");

        var netMonitoring = new NetMonitoringService(
            backend,
            new PrinterDiscoveryService(collector, new PrinterDriverSelector()),
            queue,
            Options.Create(new AgentOptions()),
            NullLogger<NetMonitoringService>.Instance);

        var resender = new QueueResender(queue, backend, NullLogger<QueueResender>.Instance);

        // Ciclo de coleta: sonda a impressora e enfileira a submissão.
        var enqueued = await netMonitoring.RunCycleOnceAsync(CancellationToken.None);
        Assert.Equal(1, enqueued);
        Assert.Equal(1, await queue.CountAsync(CancellationToken.None));

        // Ciclo de comunicação: drena a fila para o backend.
        var sent = await resender.ResendBatchAsync(CancellationToken.None);
        Assert.Equal(1, sent);
        Assert.Equal(0, await queue.CountAsync(CancellationToken.None));

        // O backend "recebeu" exatamente uma submissão, com contadores e suprimentos.
        var received = Assert.Single(backend.SubmittedPayloads);
        Assert.Contains("1234", received);
        Assert.Contains("Black Toner", received);
        Assert.Contains("\"percent\":8", received);

        // Reenviar novamente (ex.: reinício do agente) não duplica no backend, pois
        // a fila já confirmou e removeu o item (idempotência do lado do agente).
        var secondDrain = await resender.ResendBatchAsync(CancellationToken.None);
        Assert.Equal(0, secondDrain);
        Assert.Single(backend.SubmittedPayloads);
    }

    private sealed class RecordingBackend(AgentConfig config) : IBackendClient
    {
        public List<string> SubmittedPayloads { get; } = [];

        public Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct)
        {
            SubmittedPayloads.Add(payloadJson);
            return Task.FromResult(true);
        }

        public Task<AgentConfig?> GetConfigAsync(CancellationToken ct) => Task.FromResult<AgentConfig?>(config);
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
