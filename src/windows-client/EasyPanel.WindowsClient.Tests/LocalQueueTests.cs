using EasyPanel.WindowsClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes da fila local e do reenvio (Task 8.2 — R5.4/R5.5): persistência offline,
/// idempotência de enfileiramento, reenvio confirmado remove itens e falha de rede
/// mantém os itens para retentativa.
/// </summary>
public sealed class LocalQueueTests
{
    private static SqliteLocalQueue NewQueue() =>
        // Cada teste usa um banco in-memory isolado e nomeado (compartilhado só
        // enquanto a conexão do queue viver).
        new($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");

    [Fact]
    public async Task Enqueue_Persists_AndCounts()
    {
        using var queue = NewQueue();

        await queue.EnqueueAsync(new QueuedCollection("k1", "{}", DateTimeOffset.UtcNow), CancellationToken.None);
        await queue.EnqueueAsync(new QueuedCollection("k2", "{}", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(2, await queue.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Enqueue_DuplicateKey_IsIdempotent()
    {
        using var queue = NewQueue();

        await queue.EnqueueAsync(new QueuedCollection("dup", "{}", DateTimeOffset.UtcNow), CancellationToken.None);
        await queue.EnqueueAsync(new QueuedCollection("dup", "{}", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(1, await queue.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Resend_Success_RemovesItems()
    {
        using var queue = NewQueue();
        await queue.EnqueueAsync(new QueuedCollection("a", "{}", DateTimeOffset.UtcNow), CancellationToken.None);
        await queue.EnqueueAsync(new QueuedCollection("b", "{}", DateTimeOffset.UtcNow), CancellationToken.None);

        var backend = new FakeBackend(alwaysOk: true);
        var resender = new QueueResender(queue, backend, NullLogger<QueueResender>.Instance);

        var sent = await resender.ResendBatchAsync(CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equal(0, await queue.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Resend_NetworkFailure_KeepsItemsQueued()
    {
        using var queue = NewQueue();
        await queue.EnqueueAsync(new QueuedCollection("a", "{}", DateTimeOffset.UtcNow), CancellationToken.None);
        await queue.EnqueueAsync(new QueuedCollection("b", "{}", DateTimeOffset.UtcNow), CancellationToken.None);

        var backend = new FakeBackend(alwaysOk: false);
        var resender = new QueueResender(queue, backend, NullLogger<QueueResender>.Instance);

        var sent = await resender.ResendBatchAsync(CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Equal(2, await queue.CountAsync(CancellationToken.None));
    }

    private sealed class FakeBackend(bool alwaysOk) : IBackendClient
    {
        public Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct) =>
            Task.FromResult(alwaysOk);

        public Task<AgentConfig?> GetConfigAsync(CancellationToken ct) =>
            Task.FromResult<AgentConfig?>(null);
    }
}
