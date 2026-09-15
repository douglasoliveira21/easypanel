using EasyPanel.WindowsClient;

namespace EasyPanel.WindowsClient.Tests;

/// <summary>
/// Testes do <see cref="SingleInstanceGuard"/> (Task 8.1 — R2.1): a primeira
/// instância detém a posse; uma segunda com o mesmo nome não; liberar permite
/// nova aquisição.
/// </summary>
public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondInstance_SameName_DoesNotAcquire()
    {
        var name = $"test-{Guid.NewGuid():N}";

        using var first = SingleInstanceGuard.Acquire(name);
        Assert.True(first.IsOwner);

        using var second = SingleInstanceGuard.Acquire(name);
        Assert.False(second.IsOwner);
    }

    [Fact]
    public void AfterDispose_NewInstance_CanAcquire()
    {
        var name = $"test-{Guid.NewGuid():N}";

        var first = SingleInstanceGuard.Acquire(name);
        Assert.True(first.IsOwner);
        first.Dispose();

        using var second = SingleInstanceGuard.Acquire(name);
        Assert.True(second.IsOwner);
    }
}
