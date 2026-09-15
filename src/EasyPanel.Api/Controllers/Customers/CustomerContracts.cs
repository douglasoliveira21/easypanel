using EasyPanel.Modules.Customers;

namespace EasyPanel.Api.Controllers.Customers;

/// <summary>
/// Corpo da requisição de criação de cliente na camada de API (R8.1, R12.1). É um
/// contrato de API distinto do <see cref="CreateCustomerRequest"/> de domínio: o
/// controller o valida (via FluentValidation — R12.2) e o mapeia para o record de
/// domínio antes de chamar o <see cref="ICustomerService"/>. O tenant <b>não</b> é
/// informado aqui — é derivado do contexto autenticado (R6.3/R8.1). O CNPJ pode vir
/// formatado; o serviço o valida e normaliza (R8.4).
/// </summary>
public sealed record CreateCustomerApiRequest(
    string RazaoSocial,
    string Cnpj,
    string? NomeFantasia,
    string? InscricaoEstadual,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    string? Observacoes,
    CustomerStatus Status = CustomerStatus.Ativo);

/// <summary>
/// Corpo da requisição de atualização de cliente na camada de API (R8.6, R12.1).
/// Contrato distinto do <see cref="UpdateCustomerRequest"/> de domínio. Não inclui
/// o status (alterado pelo endpoint dedicado de status — R8.9) nem o tenant
/// (imutável).
/// </summary>
public sealed record UpdateCustomerApiRequest(
    string RazaoSocial,
    string Cnpj,
    string? NomeFantasia,
    string? InscricaoEstadual,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade,
    string? Estado,
    string? Cep,
    string? Observacoes);

/// <summary>
/// Corpo da requisição de alteração de status de um cliente (R8.9, R12.1). Carrega
/// apenas o novo status; o serviço valida que pertence ao conjunto fechado
/// {Ativo, Inativo, Bloqueado} (R8.3).
/// </summary>
/// <param name="Status">Novo status do cliente.</param>
public sealed record ChangeCustomerStatusApiRequest(CustomerStatus Status);

/// <summary>
/// Projeção de saída de um cliente na camada de API (R12.1). Espelha o
/// <see cref="CustomerDto"/> de domínio em um contrato de API estável. O CNPJ é
/// retornado na forma normalizada (somente dígitos) tal como armazenado.
/// </summary>
public sealed record CustomerResponse(
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

/// <summary>
/// Página de resultados da listagem de clientes (R8.8/R12.4). Espelha um
/// <see cref="EasyPanel.Shared.Kernel.Pagination.PagedResult{T}"/> de
/// <see cref="CustomerResponse"/> em um contrato de API estável.
/// </summary>
public sealed record CustomerPageResponse(
    IReadOnlyList<CustomerResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
