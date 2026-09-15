using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Customers;

/// <summary>
/// Cadastro (CRUD) de locais (pontos de instalação) do tenant do contexto
/// autenticado (R9): criação vinculada a um cliente do próprio tenant,
/// atualização, consulta por id, listagem paginada por cliente e alteração de
/// status. A implementação retorna sempre um <see cref="Result"/>/<see cref="Result{T}"/>
/// para que a camada de API mapeie falhas em códigos HTTP sem vazar dados
/// sensíveis.
///
/// <para><b>Origem do tenant (R9.1/R6.3).</b> Todo local é vinculado ao
/// <c>TenantId</c> do <see cref="EasyPanel.Modules.Tenancy.ITenantContext"/>,
/// nunca a um valor fornecido pelo chamador. Como <see cref="Location"/> é uma
/// <c>TenantEntity</c>, as leituras já são auto-escopadas pelo filtro global do
/// ORM e a escrita é validada pelo interceptor de <c>SaveChanges</c>; um local de
/// outro tenant é indistinguível de um inexistente
/// (<see cref="LocationErrors.NotFound"/> → HTTP 404), evitando enumeração
/// cross-tenant (R9.6).</para>
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Cria um local vinculado a um cliente existente do próprio tenant (R9.1). O
    /// cliente referenciado é validado contra o tenant do contexto; inexistente ou
    /// de outro tenant → <see cref="LocationErrors.CustomerNotFound"/> (HTTP 400 —
    /// R9.4). O <c>TenantId</c> é carimbado pelo interceptor de escrita.
    /// </summary>
    Task<Result<LocationDto>> CreateAsync(CreateLocationRequest req, CancellationToken ct);

    /// <summary>
    /// Atualiza um local do próprio tenant (R9.5). Inexistente/de outro tenant →
    /// <see cref="LocationErrors.NotFound"/> (HTTP 404 — R9.6). O vínculo com o
    /// cliente não é alterado.
    /// </summary>
    Task<Result<LocationDto>> UpdateAsync(Guid id, UpdateLocationRequest req, CancellationToken ct);

    /// <summary>
    /// Consulta um local por identificador, restrito ao tenant do contexto (R9.6).
    /// Inexistente/de outro tenant → <see cref="LocationErrors.NotFound"/>.
    /// </summary>
    Task<Result<LocationDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lista os locais de um cliente do tenant do contexto (R9.7), em uma
    /// <see cref="PagedResult{T}"/> com <c>PageSize</c> já limitado a ≤ 100 (R12.4)
    /// e ordenação determinística por nome. Se o cliente não pertencer ao tenant, a
    /// listagem retorna vazia (não-vazamento).
    /// </summary>
    Task<Result<PagedResult<LocationDto>>> ListByCustomerAsync(Guid customerId, PageRequest query, CancellationToken ct);

    /// <summary>
    /// Altera o status de um local do próprio tenant (R9.8). Valida que o status
    /// pertence ao conjunto fechado; inexistente/de outro tenant →
    /// <see cref="LocationErrors.NotFound"/>.
    /// </summary>
    Task<Result<LocationDto>> ChangeStatusAsync(Guid id, LocationStatus status, CancellationToken ct);
}
