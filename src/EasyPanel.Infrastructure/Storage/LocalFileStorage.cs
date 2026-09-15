using EasyPanel.Infrastructure.Health;
using EasyPanel.Shared.Kernel.Results;
using EasyPanel.Shared.Kernel.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Storage;

/// <summary>
/// Implementação de <see cref="IFileStorage"/> sobre o disco local
/// (<see cref="StorageOptions.LocalBasePath"/>), alternativa ao
/// <see cref="MinioFileStorage"/> para deploys sem um serviço S3 dedicado —
/// ex. atrás de um painel de hospedagem que já gerencia volumes persistentes
/// (ver <c>Storage:Provider = "Local"</c>). Cada chave vira um caminho de
/// arquivo relativo ao diretório base; diretórios intermediários são criados
/// sob demanda.
///
/// <para><b>Proteção contra path traversal.</b> Toda chave é resolvida e
/// validada para permanecer dentro do diretório base antes de qualquer
/// leitura/escrita — defesa em profundidade, já que a camada de domínio
/// (ex.: <c>TicketAttachmentService</c>) já sanitiza nomes de arquivo antes
/// de montar a chave.</para>
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<StorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _basePath = Path.GetFullPath(options.Value.LocalBasePath);
        _logger = logger;

        Directory.CreateDirectory(_basePath);
    }

    /// <inheritdoc />
    public async Task<Result> UploadAsync(string key, Stream content, string contentType, long sizeBytes, CancellationToken ct)
    {
        if (!TryResolvePath(key, out var path))
        {
            return Result.Failure(FileStorageErrors.Failure);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using var file = File.Create(path);
            await content.CopyToAsync(file, ct).ConfigureAwait(false);

            return Result.Success();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Falha ao gravar arquivo no storage local sob a chave {Key}.", key);
            return Result.Failure(FileStorageErrors.Failure);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Falha ao gravar arquivo no storage local sob a chave {Key}.", key);
            return Result.Failure(FileStorageErrors.Failure);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct)
    {
        if (!TryResolvePath(key, out var path) || !File.Exists(path))
        {
            return Result.Failure<Stream>(FileStorageErrors.NotFound);
        }

        try
        {
            var buffer = new MemoryStream();

            await using (var file = File.OpenRead(path))
            {
                await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
            }

            buffer.Position = 0;
            return Result.Success<Stream>(buffer);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Falha ao ler arquivo do storage local sob a chave {Key}.", key);
            return Result.Failure<Stream>(FileStorageErrors.Failure);
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(string key, CancellationToken ct)
    {
        if (!TryResolvePath(key, out var path))
        {
            return Task.FromResult(Result.Failure(FileStorageErrors.Failure));
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return Task.FromResult(Result.Success());
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Falha ao remover arquivo do storage local sob a chave {Key}.", key);
            return Task.FromResult(Result.Failure(FileStorageErrors.Failure));
        }
    }

    /// <summary>
    /// Resolve <paramref name="key"/> para um caminho absoluto dentro de
    /// <see cref="_basePath"/>, recusando qualquer chave que tente escapar do
    /// diretório base (ex.: segmentos <c>..</c>).
    /// </summary>
    private bool TryResolvePath(string key, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var relative = key.Replace('\\', '/').TrimStart('/');
        var combined = Path.GetFullPath(Path.Combine(_basePath, relative));

        var baseWithSeparator = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(baseWithSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        fullPath = combined;
        return true;
    }
}
