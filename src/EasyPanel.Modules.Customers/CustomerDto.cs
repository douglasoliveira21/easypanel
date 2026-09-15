namespace EasyPanel.Modules.Customers;

/// <summary>
/// Projeção de leitura de um <see cref="Customer"/> exposta pelo
/// <see cref="ICustomerService"/> (R8/R12.1). Contrato de domínio distinto da
/// entidade de persistência. O <see cref="Cnpj"/> é retornado na forma
/// normalizada (somente dígitos) tal como armazenado.
/// </summary>
public sealed record CustomerDto(
    Guid Id,
    Guid TenantId,
    string RazaoSocial,
    string? NomeFantasia,
    string Cnpj,
    string? InscricaoEstadual,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    string? Observacoes,
    CustomerStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
