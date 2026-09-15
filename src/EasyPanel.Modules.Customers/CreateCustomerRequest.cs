namespace EasyPanel.Modules.Customers;

/// <summary>
/// Dados de entrada para a criação de um <see cref="Customer"/> (R8.1). O
/// <c>TenantId</c> <b>não</b> é informado aqui: é sempre derivado do contexto
/// autenticado pelo <see cref="ICustomerService"/> (R8.1/R6.3), nunca do chamador.
///
/// <para>O <see cref="Cnpj"/> pode vir formatado (com pontuação); o serviço o
/// valida e normaliza para somente dígitos antes de persistir (R8.4).</para>
/// </summary>
public sealed record CreateCustomerRequest(
    string RazaoSocial,
    string Cnpj,
    string? NomeFantasia = null,
    string? InscricaoEstadual = null,
    string? Telefone = null,
    string? Email = null,
    string? Endereco = null,
    string? Cidade = null,
    string? Estado = null,
    string? Cep = null,
    string? Observacoes = null,
    CustomerStatus Status = CustomerStatus.Ativo);
