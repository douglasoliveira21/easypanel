using EasyPanel.Modules.Contracts;

namespace EasyPanel.Api.Controllers.Contracts;

/// <summary>Projeção de saída de uma <see cref="ContractFranchise"/> (Fase 7 — R3).</summary>
public sealed record ContractFranchiseResponse(
    Guid Id,
    Guid ContractId,
    ContractCounterType CounterType,
    string? CounterTypeLabel,
    long IncludedQuantity,
    decimal ExcessUnitPrice,
    string Currency);

/// <summary>Corpo da requisição de upsert (por tipo de contador) de uma franquia (R3.1/R3.2).</summary>
public sealed record SetContractFranchiseApiRequest(
    ContractCounterType CounterType,
    string? CounterTypeLabel,
    long IncludedQuantity,
    decimal ExcessUnitPrice);
