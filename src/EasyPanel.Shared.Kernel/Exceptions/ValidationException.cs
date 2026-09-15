namespace EasyPanel.Shared.Kernel.Exceptions;

/// <summary>
/// Lançada quando a entrada de uma operação é inválida. Carrega erros por
/// campo e é mapeada para HTTP 400 (ValidationProblemDetails).
/// </summary>
public sealed class ValidationException : DomainException
{
    /// <summary>Cria a exceção com uma mensagem geral e nenhum erro por campo.</summary>
    public ValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    /// <summary>Cria a exceção com um mapa de erros por campo.</summary>
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Uma ou mais falhas de validação ocorreram.")
    {
        Errors = errors;
    }

    /// <summary>Erros de validação agrupados por nome de campo.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
