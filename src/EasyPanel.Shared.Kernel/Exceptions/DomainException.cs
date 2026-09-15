namespace EasyPanel.Shared.Kernel.Exceptions;

/// <summary>
/// Exceção base para condições excepcionais de domínio, traduzidas em
/// respostas HTTP pelo tratamento centralizado de erros.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>Cria uma exceção de domínio com uma mensagem.</summary>
    protected DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Cria uma exceção de domínio com mensagem e causa interna.</summary>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
