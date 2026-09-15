using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Shared.Kernel.Storage;

/// <summary>Erros de domínio de <see cref="IFileStorage"/> (Fase 6), no padrão de
/// <c>TicketingErrors</c>/<c>InventoryErrors</c>.</summary>
public static class FileStorageErrors
{
    /// <summary>Chave inexistente no storage. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "storage.not_found",
        "Arquivo não encontrado no armazenamento.");

    /// <summary>Falha ao gravar ou ler no storage (conectividade, credenciais, bucket). → 400.</summary>
    public static readonly Error Failure = Error.Validation(
        "storage.failure",
        "Não foi possível concluir a operação no armazenamento.");
}
