using EasyPanel.WindowsClient.Snmp;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes da descoberta de impressoras (Task 8.3 — R7): candidatas que respondem
/// são identificadas com driver; alvos ignorados são pulados; alvos sem resposta
/// não viram candidatas; a coleta interpreta as leituras.
/// </summary>
public sealed class PrinterDiscoveryTests
{
    [Fact]
    public async Task Discover_ReturnsRespondingCandidates_WithDriver()
    {
        var collector = new FakeCollector();
        collector.Probes["10.0.0.1"] = new DeviceProbe("HP LaserJet", null, null);
        collector.Probes["10.0.0.2"] = new DeviceProbe("Brother HL", null, null);

        var service = new PrinterDiscoveryService(collector, new PrinterDriverSelector());

        var candidates = await service.DiscoverAsync(
            ["10.0.0.1", "10.0.0.2"],
            new HashSet<string>(),
            SnmpCredentials.DefaultV2c,
            CancellationToken.None);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Host == "10.0.0.1" && c.Driver == "HP");
        Assert.Contains(candidates, c => c.Host == "10.0.0.2" && c.Driver == "Brother");
    }

    [Fact]
    public async Task Discover_SkipsIgnoredTargets()
    {
        var collector = new FakeCollector();
        collector.Probes["10.0.0.1"] = new DeviceProbe("HP LaserJet", null, null);

        var service = new PrinterDiscoveryService(collector, new PrinterDriverSelector());

        var candidates = await service.DiscoverAsync(
            ["10.0.0.1"],
            new HashSet<string> { "10.0.0.1" },
            SnmpCredentials.DefaultV2c,
            CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Discover_NonResponding_NotACandidate()
    {
        var collector = new FakeCollector(); // sem probe → lança
        var service = new PrinterDiscoveryService(collector, new PrinterDriverSelector());

        var candidates = await service.DiscoverAsync(
            ["10.0.0.99"],
            new HashSet<string>(),
            SnmpCredentials.DefaultV2c,
            CancellationToken.None);

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Collect_InterpretsReadingWithSelectedDriver()
    {
        var collector = new FakeCollector();
        collector.Probes["10.0.0.1"] = new DeviceProbe("HP LaserJet", null, null);
        collector.Values["10.0.0.1"] = new Dictionary<string, string>
        {
            [StandardOids.SerialNumber] = "SN9",
            [StandardOids.DeviceStatus] = "2",
            ["1.3.6.1.2.1.43.10.2.1.4.1.1"] = "700",
        };

        var service = new PrinterDiscoveryService(collector, new PrinterDriverSelector());
        var candidate = new PrinterCandidate("10.0.0.1", collector.Probes["10.0.0.1"], "HP");

        var reading = await service.CollectAsync(candidate, SnmpCredentials.DefaultV2c, CancellationToken.None);

        Assert.Equal("SN9", reading.SerialNumber);
        Assert.Equal(PrinterStatusKind.Online, reading.Status);
        Assert.Contains(reading.Counters, c => c.Value == 700);
    }

    private sealed class FakeCollector : ISnmpCollector
    {
        public Dictionary<string, DeviceProbe> Probes { get; } = new();

        public Dictionary<string, IReadOnlyDictionary<string, string>> Values { get; } = new();

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
            var values = Values.TryGetValue(host, out var v)
                ? v
                : new Dictionary<string, string>();
            return Task.FromResult(new SnmpResult(values));
        }
    }
}
