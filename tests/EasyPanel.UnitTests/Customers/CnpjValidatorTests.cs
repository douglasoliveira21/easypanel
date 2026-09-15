using EasyPanel.Modules.Customers;

namespace EasyPanel.UnitTests.Customers;

/// <summary>
/// Testes de unidade do <see cref="CnpjValidator"/> (R8.4): normalização
/// (somente dígitos), comprimento, sequência repetida e conferência dos dois
/// dígitos verificadores por módulo 11.
/// </summary>
public sealed class CnpjValidatorTests
{
    // CNPJs válidos conhecidos (dígitos verificadores corretos).
    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("04252011000110")]
    [InlineData("04.252.011/0001-10")]
    public void IsValid_WithValidCnpj_ReturnsTrue(string input)
    {
        Assert.True(CnpjValidator.IsValid(input));
    }

    [Fact]
    public void IsValid_WithInvalidCheckDigits_ReturnsFalse()
    {
        // Mesmo prefixo do CNPJ válido, mas dígitos verificadores errados.
        Assert.False(CnpjValidator.IsValid("11222333000182"));
        Assert.False(CnpjValidator.IsValid("11222333000100"));
    }

    [Theory]
    [InlineData("1122233300018")]    // 13 dígitos
    [InlineData("112223330001811")]  // 15 dígitos
    [InlineData("")]
    [InlineData("abc")]
    public void IsValid_WithWrongLength_ReturnsFalse(string input)
    {
        Assert.False(CnpjValidator.IsValid(input));
    }

    [Fact]
    public void IsValid_WithNull_ReturnsFalse()
    {
        Assert.False(CnpjValidator.IsValid(null));
    }

    [Theory]
    [InlineData("00000000000000")]
    [InlineData("11111111111111")]
    [InlineData("99999999999999")]
    public void IsValid_WithAllSameDigits_ReturnsFalse(string input)
    {
        // Sequências repetidas satisfazem o módulo 11 mas não são CNPJs válidos.
        Assert.False(CnpjValidator.IsValid(input));
    }

    [Fact]
    public void Normalize_StripsPunctuationAndKeepsDigits()
    {
        Assert.Equal("11222333000181", CnpjValidator.Normalize("11.222.333/0001-81"));
        Assert.Equal("11222333000181", CnpjValidator.Normalize(" 11 222 333 0001 81 "));
    }

    [Fact]
    public void Normalize_WithNullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CnpjValidator.Normalize(null));
        Assert.Equal(string.Empty, CnpjValidator.Normalize(string.Empty));
    }

    [Fact]
    public void TryNormalize_WithFormattedValidCnpj_ReturnsCanonicalDigits()
    {
        var ok = CnpjValidator.TryNormalize("11.222.333/0001-81", out var normalized);

        Assert.True(ok);
        Assert.Equal("11222333000181", normalized);
    }

    [Fact]
    public void TryNormalize_WithInvalidCnpj_ReturnsFalseAndEmpty()
    {
        var ok = CnpjValidator.TryNormalize("11.222.333/0001-82", out var normalized);

        Assert.False(ok);
        Assert.Equal(string.Empty, normalized);
    }
}
