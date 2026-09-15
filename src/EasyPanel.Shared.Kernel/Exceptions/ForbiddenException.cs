namespace EasyPanel.Shared.Kernel.Exceptions;

/// <summary>
/// Lançada quando um usuário autenticado não possui permissão para a operação.
/// Mapeada para HTTP 403.
/// </summary>
public sealed class ForbiddenException : DomainException
{
    /// <summary>Cria a exceção com uma mensagem descritiva.</summary>
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
