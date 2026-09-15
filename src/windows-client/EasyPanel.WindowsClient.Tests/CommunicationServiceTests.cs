using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes do <see cref="CommunicationService"/> (Task 7.2 — R1.4): um item
/// enfileirado é drenado e confirmado (removido da fila) após o laço interno
/// iniciar via <see cref="ISupervisedService.RestartAsync"/>.
/// </summary>
public sealed class CommunicationServiceTests
{
    [Fact]
    public async Task Restart_DrainsQueuedItem_AndBecomesHealthy()
    {
        using var queue = new SqliteLocalQueue($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        await queue.EnqueueAsync(new QueuedCollection("k1", "{}", DateTimeOffset.UtcNow), CancellationToken.None);

        var backend = new AlwaysOkBackend();
        var resender = new QueueResender(queue, backend, NullLogger<QueueResender>.Instance);
        var options = Options.Create(new AgentOptions { ResendIntervalSeconds = 60 });
        var service = new CommunicationService(resender, options, NullLogger<CommunicationService>.Instance);

        Assert.False(service.IsHealthy);

        await service.RestartAsync(CancellationToken.None);
        Assert.True(service.IsHealthy);

        // Aguarda a primeira drenagem do laço interno (roda imediatamente ao iniciar).
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (await queue.CountAsync(CancellationToken.None) > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.Equal(0, await queue.CountAsync(CancellationToken.None));
    }

    private sealed class AlwaysOkBackend : IBackendClient
    {
        public Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<AgentConfig?> GetConfigAsync(CancellationToken ct) => Task.FromResult<AgentConfig?>(null);
    }
}
