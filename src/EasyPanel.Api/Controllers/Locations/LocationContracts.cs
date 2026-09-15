using EasyPanel.Modules.Customers;

namespace EasyPanel.Api.Controllers.Locations;

/// <summary>
/// Corpo da requisição de criação de local na camada de API (R9.1, R12.1). É um
/// contrato de API distinto do <see cref="CreateLocationRequest"/> de domínio: o
/// controller o valida (via FluentValidation — R12.2) e o mapeia para o record de
/// domínio antes de chamar o <see cref="ILocationService"/>. O tenant <b>não</b> é
/// informado aqui — é derivado do contexto autenticado (R6.3/R9.1). O
/// <see cref="CustomerId"/> deve referenciar um cliente do próprio tenant (R9.4).
/// </summary>
public sealed record CreateLocationApiRequest(
    Guid CustomerId,
    string Nome,
    string? Endereco,
    string? Responsavel,
    string? Telefone,
    string? Email,
    string? Observacoes,
    LocationStatus Status = LocationStatus.Ativo);

/// <summary>
/// Corpo da requisição de atualização de local na camada de API (R9.5, R12.1).
/// Contrato distinto do <see cref="UpdateLocationRequest"/> de domínio. Não inclui
/// o status (alterado pelo endpoint dedicado — R9.8), o tenant (imutável) nem o
/// vínculo com o cliente (imutável).
/// </summary>
public sealed record UpdateLocationApiRequest(
    string Nome,
    string? Endereco,
    string? Responsavel,
    string? Telefone,
    string? Email,
    string? Observacoes);

/// <summary>
/// Corpo da requisição de alteração de status de um local (R9.8, R12.1). Carrega
/// apenas o novo status; o serviço valida que pertence ao conjunto fechado
/// {Ativo, Inativo}.
/// </summary>
/// <param name="Status">Novo status do local.</param>
public sealed record ChangeLocationStatusApiRequest(LocationStatus Status);

/// <summary>
/// Projeção de saída de um local na camada de API (R12.1). Espelha o
/// <see cref="LocationDto"/> de domínio em um contrato de API estável.
/// </summary>
public sealed record LocationResponse(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    string Nome,
    string? Endereco,
    string? Responsavel,
    string? Telefone,
    string? Email,
    string? Observacoes,
    LocationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Página de resultados da listagem de locais (R9.7/R12.4). Espelha um
/// <see cref="EasyPanel.Shared.Kernel.Pagination.PagedResult{T}"/> de
/// <see cref="LocationResponse"/> em um contrato de API estável.
/// </summary>
public sealed record LocationPageResponse(
    IReadOnlyList<LocationResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
