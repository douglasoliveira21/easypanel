using EasyPanel.Infrastructure.Health;
using EasyPanel.Shared.Kernel.Results;
using EasyPanel.Shared.Kernel.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace EasyPanel.Infrastructure.Storage;

/// <summary>
/// Implementação de <see cref="IFileStorage"/> sobre o MinIO (S3-compatible),
/// via SDK oficial <c>Minio</c> (Fase 6 — R5). Primeiro uso funcional do storage
/// provisionado desde a Fase 1 (antes, apenas health check). Credenciais nunca
/// são logadas.
/// </summary>
public sealed class MinioFileStorage : IFileStorage
{
    private readonly StorageOptions _options;
    private readonly ILogger<MinioFileStorage> _logger;
    private readonly Lazy<IMinioClient> _client;
    private volatile bool _bucketEnsured;

    public MinioFileStorage(IOptions<StorageOptions> options, ILogger<MinioFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<IMinioClient>(BuildClient);
    }

    /// <inheritdoc />
    public async Task<Result> UploadAsync(string key, Stream content, string contentType, long sizeBytes, CancellationToken ct)
    {
        try
        {
            await EnsureBucketAsync(ct).ConfigureAwait(false);

            await _client.Value.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(key)
                    .WithStreamData(content)
                    .WithObjectSize(sizeBytes)
                    .WithContentType(contentType),
                ct).ConfigureAwait(false);

            return Result.Success();
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "Falha ao gravar objeto no storage sob a chave {Key}.", key);
            return Result.Failure(FileStorageErrors.Failure);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct)
    {
        try
        {
            var buffer = new MemoryStream();

            await _client.Value.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(key)
                    .WithCallbackStream(stream => stream.CopyTo(buffer)),
                ct).ConfigureAwait(false);

            buffer.Position = 0;
            return Result.Success<Stream>(buffer);
        }
        catch (ObjectNotFoundException)
        {
            return Result.Failure<Stream>(FileStorageErrors.NotFound);
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "Falha ao ler objeto do storage sob a chave {Key}.", key);
            return Result.Failure<Stream>(FileStorageErrors.Failure);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            await _client.Value.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(_options.Bucket).WithObject(key),
                ct).ConfigureAwait(false);

            return Result.Success();
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "Falha ao remover objeto do storage sob a chave {Key}.", key);
            return Result.Failure(FileStorageErrors.Failure);
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketEnsured)
        {
            return;
        }

        var exists = await _client.Value
            .BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.Bucket), ct)
            .ConfigureAwait(false);

        if (!exists)
        {
            await _client.Value
                .MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.Bucket), ct)
                .ConfigureAwait(false);
        }

        _bucketEnsured = true;
    }

    private IMinioClient BuildClient()
    {
        var endpoint = new Uri(_options.Endpoint, UriKind.Absolute);
        var hostAndPort = endpoint.IsDefaultPort ? endpoint.Host : $"{endpoint.Host}:{endpoint.Port}";
        var useSsl = string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase);

        return new MinioClient()
            .WithEndpoint(hostAndPort)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(useSsl)
            .Build();
    }
}
