namespace EasyPanel.Shared.Kernel.Exceptions;

/// <summary>
/// Lançada quando uma operação viola uma restrição de unicidade ou estado.
/// Mapeada para HTTP 409.
/// </summary>
public sealed class ConflictException : DomainException
{
    /// <summary>Cria a exceção com uma mensagem descritiva.</summary>
    public ConflictException(string message)
        : base(message)
    {
    }
}
