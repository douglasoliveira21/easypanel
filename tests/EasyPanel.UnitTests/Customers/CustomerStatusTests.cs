using EasyPanel.Modules.Customers;

namespace EasyPanel.UnitTests.Customers;

/// <summary>
/// Testes de unidade das regras de status de <see cref="Customer"/> (R8.3): o
/// status é um conjunto fechado {Ativo, Inativo, Bloqueado}. A validação de
/// pertinência ao conjunto (usada pelo serviço em create/change-status) é
/// exercitada aqui de forma isolada, sem depender de banco.
/// </summary>
public sealed class CustomerStatusTests
{
    [Fact]
    public void CustomerStatus_HasExactlyThreeMembers()
    {
        var values = Enum.GetValues<CustomerStatus>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(CustomerStatus.Ativo, 0)]
    [InlineData(CustomerStatus.Inativo, 1)]
    [InlineData(CustomerStatus.Bloqueado, 2)]
    public void CustomerStatus_HasStableIntValues(CustomerStatus status, int expected)
    {
        // Os inteiros são persistidos no banco; sua estabilidade é parte do contrato.
        Assert.Equal(expected, (int)status);
    }

    [Theory]
    [InlineData(CustomerStatus.Ativo)]
    [InlineData(CustomerStatus.Inativo)]
    [InlineData(CustomerStatus.Bloqueado)]
    public void IsDefined_ForClosedSetMembers_ReturnsTrue(CustomerStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(999)]
    public void IsDefined_ForOutOfRangeValue_ReturnsFalse(int raw)
    {
        // Um cast arbitrário de int não deve ser tratado como status válido (R8.3).
        Assert.False(Enum.IsDefined((CustomerStatus)raw));
    }
}
