using EasyPanel.WindowsClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes do <see cref="Guardian"/> (Task 8.1 — R2.2): serviços não saudáveis são
/// reiniciados; saudáveis são ignorados; falha de reinício não interrompe os demais.
/// </summary>
public sealed class GuardianTests
{
    [Fact]
    public async Task SuperviseOnce_RestartsUnhealthyService()
    {
        var unhealthy = new FakeService("net", healthy: false);
        var healthy = new FakeService("comm", healthy: true);
        var guardian = new Guardian([unhealthy, healthy], NullLogger<Guardian>.Instance);

        var restarted = await guardian.SuperviseOnceAsync(CancellationToken.None);

        Assert.Equal(1, restarted);
        Assert.Equal(1, unhealthy.RestartCount);
        Assert.Equal(0, healthy.RestartCount);
    }

    [Fact]
    public async Task SuperviseOnce_RestartFailure_DoesNotStopOthers()
    {
        var failing = new FakeService("net", healthy: false, throwOnRestart: true);
        var other = new FakeService("comm", healthy: false);
        var guardian = new Guardian([failing, other], NullLogger<Guardian>.Instance);

        var restarted = await guardian.SuperviseOnceAsync(CancellationToken.None);

        // Apenas o segundo reinicia com sucesso; a falha do primeiro é registrada.
        Assert.Equal(1, restarted);
        Assert.Equal(1, other.RestartCount);
    }

    private sealed class FakeService(string name, bool healthy, bool throwOnRestart = false)
        : ISupervisedService
    {
        public string Name => name;

        public bool IsHealthy => healthy;

        public int RestartCount { get; private set; }

        public Task RestartAsync(CancellationToken ct)
        {
            if (throwOnRestart)
            {
                throw new InvalidOperationException("falha simulada");
            }

            RestartCount++;
            return Task.CompletedTask;
        }
    }
}
