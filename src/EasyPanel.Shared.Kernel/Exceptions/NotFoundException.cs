namespace EasyPanel.Shared.Kernel.Exceptions;

/// <summary>
/// Lançada quando um recurso solicitado não existe. Mapeada para HTTP 404.
/// </summary>
public sealed class NotFoundException : DomainException
{
    /// <summary>Cria a exceção com uma mensagem descritiva.</summary>
    public NotFoundException(string message)
        : base(message)
    {
    }
}
