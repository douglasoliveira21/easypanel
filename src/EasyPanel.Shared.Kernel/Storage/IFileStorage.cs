using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Shared.Kernel.Storage;

/// <summary>
/// Abstração de armazenamento de arquivos por chave, independente do provedor
/// (Fase 6 — R5). A implementação real (sobre MinIO/S3) vive na Infrastructure;
/// este contrato não depende de nenhum SDK de storage, para que os módulos de
/// domínio (ex.: <c>Modules.Ticketing</c>) possam depender apenas dele.
/// </summary>
public interface IFileStorage
{
    /// <summary>Grava <paramref name="content"/> sob <paramref name="key"/>. Sobrescreve se já existir.</summary>
    Task<Result> UploadAsync(string key, Stream content, string contentType, long sizeBytes, CancellationToken ct);

    /// <summary>Lê o conteúdo gravado sob <paramref name="key"/>; falha (não encontrado) vira <see cref="Result{T}.IsFailure"/>.</summary>
    Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct);

    /// <summary>Remove o conteúdo gravado sob <paramref name="key"/>, se existir.</summary>
    Task<Result> DeleteAsync(string key, CancellationToken ct);
}
