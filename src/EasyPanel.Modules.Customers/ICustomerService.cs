using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Customers;

/// <summary>
/// Cadastro (CRUD) de clientes do tenant do contexto autenticado (R8): criação
/// com unicidade de CNPJ por tenant, atualização, consulta por id, listagem
/// paginada/ordenada e alteração de status. A implementação retorna sempre um
/// <see cref="Result"/>/<see cref="Result{T}"/> para que a camada de API mapeie
/// falhas em códigos HTTP sem vazar dados sensíveis.
///
/// <para><b>Origem do tenant (R8.1/R6.3).</b> Todo cliente é vinculado ao
/// <c>TenantId</c> do <see cref="EasyPanel.Modules.Tenancy.ITenantContext"/>,
/// nunca a um valor fornecido pelo chamador. Como <see cref="Customer"/> é uma
/// <c>TenantEntity</c>, as leituras já são auto-escopadas pelo filtro global do
/// ORM e a escrita é validada pelo interceptor de <c>SaveChanges</c>; um cliente
/// de outro tenant é indistinguível de um inexistente
/// (<see cref="CustomerErrors.NotFound"/> → HTTP 404), evitando enumeração
/// cross-tenant (R8.7).</para>
/// </summary>
public interface ICustomerService
{
    /// <summary>
    /// Cria um cliente vinculado ao tenant do contexto (R8.1). Valida o CNPJ
    /// (formato/dígitos — R8.4) e o normaliza; rejeita CNPJ duplicado no mesmo
    /// tenant com <see cref="CustomerErrors.DuplicateCnpj"/> (HTTP 409 — R8.5). O
    /// <c>TenantId</c> é carimbado pelo interceptor de escrita a partir do
    /// contexto; um guarda explícito retorna <see cref="CustomerErrors.NoTenantContext"/>
    /// quando não há tenant resolvido.
    /// </summary>
    Task<Result<CustomerDto>> CreateAsync(CreateCustomerRequest req, CancellationToken ct);

    /// <summary>
    /// Atualiza um cliente do próprio tenant (R8.6). Cliente inexistente ou de
    /// outro tenant → <see cref="CustomerErrors.NotFound"/> (HTTP 404 — R8.7). Se
    /// o CNPJ mudar, revalida (R8.4) e reconfere a unicidade por tenant excluindo
    /// o próprio registro (R8.5).
    /// </summary>
    Task<Result<CustomerDto>> UpdateAsync(Guid id, UpdateCustomerRequest req, CancellationToken ct);

    /// <summary>
    /// Consulta um cliente por identificador, restrito ao tenant do contexto
    /// (R8.7). Inexistente/de outro tenant → <see cref="CustomerErrors.NotFound"/>.
    /// </summary>
    Task<Result<CustomerDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lista os clientes do tenant do contexto (R8.8), em uma
    /// <see cref="PagedResult{T}"/> com <c>PageSize</c> já limitado a ≤ 100
    /// (R12.4) e ordenação determinística por razão social.
    /// </summary>
    Task<Result<PagedResult<CustomerDto>>> ListAsync(PageRequest query, CancellationToken ct);

    /// <summary>
    /// Altera o status de um cliente do próprio tenant (R8.9). Valida que o status
    /// pertence ao conjunto fechado (R8.3); inexistente/de outro tenant →
    /// <see cref="CustomerErrors.NotFound"/>.
    /// </summary>
    Task<Result<CustomerDto>> ChangeStatusAsync(Guid id, CustomerStatus status, CancellationToken ct);
}
