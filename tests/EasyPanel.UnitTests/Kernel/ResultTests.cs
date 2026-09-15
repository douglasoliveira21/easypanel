using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.UnitTests.Kernel;

/// <summary>
/// Unit tests para o padrão <see cref="Result"/> e <see cref="Result{T}"/>.
/// Cobre estados de sucesso/falha, invariantes de construção e acesso ao valor.
/// </summary>
public class ResultTests
{
    [Fact]
    public void Success_IsSuccess_AndHasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_IsFailure_AndCarriesError()
    {
        var error = Error.Conflict("customer.cnpj.duplicate", "CNPJ já cadastrado.");

        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void FailureWithNoneError_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        Result<int> result = Result.Failure<int>(Error.NotFound("customer.notfound", "Não encontrado."));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError_ProducesFailure()
    {
        Result<string> result = Error.Validation("field.invalid", "Inválido.");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Forbidden)]
    public void Error_Factories_PreserveType(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.Validation => Error.Validation("c", "m"),
            ErrorType.NotFound => Error.NotFound("c", "m"),
            ErrorType.Conflict => Error.Conflict("c", "m"),
            ErrorType.Forbidden => Error.Forbidden("c", "m"),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        Assert.Equal(type, error.Type);
        Assert.Equal("c", error.Code);
        Assert.Equal("m", error.Message);
    }
}
