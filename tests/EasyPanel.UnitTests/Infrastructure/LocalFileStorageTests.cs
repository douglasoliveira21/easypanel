using System.Text;

using EasyPanel.Infrastructure.Health;
using EasyPanel.Infrastructure.Storage;
using EasyPanel.Shared.Kernel.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyPanel.UnitTests.Infrastructure;

/// <summary>
/// Testes do <see cref="LocalFileStorage"/> (provedor <c>Local</c> de
/// <c>IFileStorage</c>): grava/lê/remove sob um diretório temporário isolado
/// por teste, e confirma a proteção contra path traversal.
/// </summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"easypanel-storage-tests-{Guid.NewGuid():N}");

        var options = Options.Create(new StorageOptions { LocalBasePath = _tempDir });
        _storage = new LocalFileStorage(options, NullLogger<LocalFileStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task UploadAsync_ThenDownloadAsync_RoundTripsContent()
    {
        var content = Encoding.UTF8.GetBytes("conteúdo de teste");
        using var stream = new MemoryStream(content);

        var uploaded = await _storage.UploadAsync(
            "tickets/t1/a1-arquivo.txt", stream, "text/plain", content.Length, CancellationToken.None);
        Assert.True(uploaded.IsSuccess);

        var downloaded = await _storage.DownloadAsync("tickets/t1/a1-arquivo.txt", CancellationToken.None);
        Assert.True(downloaded.IsSuccess);

        using var reader = new StreamReader(downloaded.Value);
        var text = await reader.ReadToEndAsync();
        Assert.Equal("conteúdo de teste", text);
    }

    [Fact]
    public async Task UploadAsync_CreatesIntermediateDirectories()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var result = await _storage.UploadAsync(
            "tickets/tenant-x/ticket-y/anexo.bin", stream, "application/octet-stream", 3, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(_tempDir, "tickets", "tenant-x", "ticket-y", "anexo.bin")));
    }

    [Fact]
    public async Task DownloadAsync_ForMissingKey_ReturnsNotFound()
    {
        var result = await _storage.DownloadAsync("nao-existe.txt", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileStorageErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndIsIdempotent()
    {
        using var stream = new MemoryStream([9]);
        await _storage.UploadAsync("a.bin", stream, "application/octet-stream", 1, CancellationToken.None);

        var firstDelete = await _storage.DeleteAsync("a.bin", CancellationToken.None);
        Assert.True(firstDelete.IsSuccess);
        Assert.False(File.Exists(Path.Combine(_tempDir, "a.bin")));

        // Remover novamente (já não existe) continua bem-sucedido (idempotente).
        var secondDelete = await _storage.DeleteAsync("a.bin", CancellationToken.None);
        Assert.True(secondDelete.IsSuccess);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("tickets/../../escape.txt")]
    [InlineData("..\\escape.txt")]
    public async Task UploadAsync_WithPathTraversalKey_Fails_AndNeverEscapesBaseDirectory(string maliciousKey)
    {
        using var stream = new MemoryStream([1]);

        var result = await _storage.UploadAsync(maliciousKey, stream, "application/octet-stream", 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FileStorageErrors.Failure.Code, result.Error.Code);

        // Nada foi escrito fora do diretório base (pai do diretório temporário).
        var parentDir = Directory.GetParent(_tempDir)!.FullName;
        Assert.DoesNotContain(
            Directory.EnumerateFiles(parentDir, "escape.txt", SearchOption.TopDirectoryOnly),
            _ => true);
    }
}
