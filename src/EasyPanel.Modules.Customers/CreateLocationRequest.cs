namespace EasyPanel.Modules.Customers;

/// <summary>
/// Dados de entrada para a criação de um <see cref="Location"/> (R9.1). O
/// <c>TenantId</c> <b>não</b> é informado aqui: é sempre derivado do contexto
/// autenticado (R9.1/R6.3). O <see cref="CustomerId"/> deve referenciar um
/// cliente existente do próprio tenant (R9.4).
/// </summary>
public sealed record CreateLocationRequest(
    Guid CustomerId,
    string Nome,
    string? Endereco = null,
    string? Responsavel = null,
    string? Telefone = null,
    string? Email = null,
    string? Observacoes = null,
    LocationStatus Status = LocationStatus.Ativo);
