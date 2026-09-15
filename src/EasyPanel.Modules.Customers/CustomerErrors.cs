using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Customers;

/// <summary>
/// Erros esperados do cadastro de clientes (R8), expostos como valores estáveis
/// para uso pelo <see cref="ICustomerService"/> e mapeamento na camada de API.
/// Cada erro carrega uma <see cref="ErrorType"/> traduzida em HTTP (400/404/409).
/// </summary>
public static class CustomerErrors
{
    /// <summary>
    /// Não há tenant resolvido no contexto para vincular o cliente (R8.1).
    /// Mapeado para HTTP 400 (validação). Um guarda explícito evita que o
    /// interceptor de escrita lance <c>CrossTenantAccessException</c>, produzindo
    /// uma falha de validação limpa.
    /// </summary>
    public static readonly Error NoTenantContext = Error.Validation(
        "customer.tenant.missing",
        "Não há tenant no contexto para criar o cliente.");

    /// <summary>CNPJ com formato/dígitos verificadores inválidos (R8.4). HTTP 400.</summary>
    public static readonly Error InvalidCnpj = Error.Validation(
        "customer.cnpj.invalid",
        "O CNPJ informado é inválido.");

    /// <summary>Razão social ausente/vazia (R8.2). HTTP 400.</summary>
    public static readonly Error MissingRazaoSocial = Error.Validation(
        "customer.razaosocial.missing",
        "A razão social é obrigatória.");

    /// <summary>Status fora do conjunto fechado {Ativo, Inativo, Bloqueado} (R8.3). HTTP 400.</summary>
    public static readonly Error InvalidStatus = Error.Validation(
        "customer.status.invalid",
        "O status informado é inválido.");

    /// <summary>Já existe um cliente com o mesmo CNPJ no tenant (R8.5). HTTP 409.</summary>
    public static readonly Error DuplicateCnpj = Error.Conflict(
        "customer.cnpj.duplicate",
        "Já existe um cliente com este CNPJ neste tenant.");

    /// <summary>
    /// Cliente inexistente ou pertencente a outro tenant (R8.7). Retornado como
    /// <see cref="ErrorType.NotFound"/> (HTTP 404) para não revelar a existência
    /// de clientes de outros tenants (não-vazamento).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "customer.not_found",
        "Cliente não encontrado.");
}
