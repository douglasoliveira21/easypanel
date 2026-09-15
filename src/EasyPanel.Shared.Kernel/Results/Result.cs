namespace EasyPanel.Shared.Kernel.Results;

/// <summary>
/// Resultado de uma operação que pode falhar com um erro esperado de domínio.
/// Evita o uso de exceções para fluxo de controle (ver design, padrão Result).
/// </summary>
public class Result
{
    /// <summary>Constrói um resultado a partir do estado de sucesso e do erro.</summary>
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Um resultado de sucesso não pode conter um erro.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Um resultado de falha deve conter um erro.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Indica que a operação foi concluída com sucesso.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indica que a operação falhou.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Erro associado à falha, ou <see cref="Error.None"/> em caso de sucesso.</summary>
    public Error Error { get; }

    /// <summary>Cria um resultado de sucesso.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Cria um resultado de falha com o erro informado.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Cria um resultado de sucesso com valor.</summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    /// <summary>Cria um resultado de falha com valor tipado.</summary>
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

/// <summary>
/// Resultado de uma operação que retorna um valor em caso de sucesso ou um
/// erro esperado de domínio em caso de falha.
/// </summary>
/// <typeparam name="T">Tipo do valor produzido em caso de sucesso.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T _value;

    private Result(T value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// Valor produzido em caso de sucesso. Lança <see cref="InvalidOperationException"/>
    /// se acessado em um resultado de falha.
    /// </summary>
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Não é possível acessar o valor de um resultado de falha.");

    /// <summary>Cria um resultado de sucesso com o valor informado.</summary>
    public static Result<T> Success(T value) => new(value, true, Error.None);

    /// <summary>Cria um resultado de falha com o erro informado.</summary>
    public static new Result<T> Failure(Error error) => new(default!, false, error);

    /// <summary>Conversão implícita de um valor para um resultado de sucesso.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Conversão implícita de um erro para um resultado de falha.</summary>
    public static implicit operator Result<T>(Error error) => Failure(error);
}
