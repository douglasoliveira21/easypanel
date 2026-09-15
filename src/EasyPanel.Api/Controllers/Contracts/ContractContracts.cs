using EasyPanel.Modules.Contracts;

namespace EasyPanel.Api.Controllers.Contracts;

/// <summary>Projeção de saída de um <see cref="Contract"/> (Fase 7 — R1, R5).</summary>
public sealed record ContractResponse(
    Guid Id,
    Guid TenantId,
    string Number,
    Guid CustomerId,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate,
    ContractStatus Status,
    string? Observations,
    bool IsExpired,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Página de resultados da listagem de contratos (R1.4, R12.4).</summary>
public sealed record ContractPageResponse(IReadOnlyList<ContractResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>Corpo da requisição de criação de um contrato (R1.2).</summary>
public sealed record CreateContractApiRequest(
    string Number,
    Guid CustomerId,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate,
    string? Observations);

/// <summary>Corpo da requisição de atualização de um contrato (R1) — <c>CustomerId</c>/<c>StartDate</c> são imutáveis.</summary>
public sealed record UpdateContractApiRequest(string Number, DateTimeOffset? EndDate, string? Observations);

/// <summary>Corpo da requisição de transição de status (R5.2).</summary>
public sealed record ChangeContractStatusApiRequest(ContractStatus ToStatus);
