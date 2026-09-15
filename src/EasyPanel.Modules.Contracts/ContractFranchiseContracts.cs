using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Contracts;

/// <summary>Projeção de leitura de uma <see cref="ContractFranchise"/> (R3).</summary>
public sealed record ContractFranchiseDto(
    Guid Id,
    Guid ContractId,
    ContractCounterType CounterType,
    string? CounterTypeLabel,
    long IncludedQuantity,
    decimal ExcessUnitPrice,
    string Currency);

/// <summary>Dados de upsert (por tipo de contador) de uma <see cref="ContractFranchise"/> (R3.1/R3.2).</summary>
public sealed record SetContractFranchiseRequest(
    ContractCounterType CounterType,
    string? CounterTypeLabel,
    long IncludedQuantity,
    decimal ExcessUnitPrice);

/// <summary>Serviço de franquia por tipo de contador de um <see cref="Contract"/> (R3).</summary>
public interface IContractFranchiseService
{
    /// <summary>Cria ou atualiza (upsert por CounterType) uma franquia, auditado (R3.3).</summary>
    Task<Result<ContractFranchiseDto>> SetAsync(Guid contractId, SetContractFranchiseRequest request, CancellationToken ct);

    /// <summary>Lista as franquias do contrato, restrito ao tenant (R3.4).</summary>
    Task<Result<IReadOnlyList<ContractFranchiseDto>>> ListAsync(Guid contractId, CancellationToken ct);
}
