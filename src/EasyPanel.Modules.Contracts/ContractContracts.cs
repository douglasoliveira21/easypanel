using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Contracts;

/// <summary>Projeção de leitura de um <see cref="Contract"/> (R1, R5).</summary>
public sealed record ContractDto(
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

/// <summary>Dados de criação de um <see cref="Contract"/> (R1.2). Tenant vem do contexto.</summary>
public sealed record CreateContractRequest(
    string Number,
    Guid CustomerId,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate,
    string? Observations);

/// <summary>Dados de atualização de um <see cref="Contract"/> (R1) — <c>CustomerId</c>/<c>StartDate</c> são imutáveis.</summary>
public sealed record UpdateContractRequest(string Number, DateTimeOffset? EndDate, string? Observations);

/// <summary>Filtro de listagem de <see cref="Contract"/> (R1.4).</summary>
public sealed record ContractQuery(PageRequest Page, Guid? CustomerId = null, ContractStatus? Status = null);

/// <summary>Dados de transição de status de um <see cref="Contract"/> (R5.2).</summary>
public sealed record ChangeContractStatusRequest(ContractStatus ToStatus);

/// <summary>Serviço de ciclo de vida de <see cref="Contract"/> (R1/R4/R5).</summary>
public interface IContractService
{
    /// <summary>Cria um contrato vinculado ao tenant do contexto (R1.2/R1.3).</summary>
    Task<Result<ContractDto>> CreateAsync(CreateContractRequest request, CancellationToken ct);

    /// <summary>Atualiza campos editáveis de um contrato do próprio tenant (R1); cross-tenant → 404.</summary>
    Task<Result<ContractDto>> UpdateAsync(Guid id, UpdateContractRequest request, CancellationToken ct);

    /// <summary>Consulta por id, restrito ao tenant (R1.4).</summary>
    Task<Result<ContractDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lista contratos com filtros/paginação, restrito ao tenant (R1.4).</summary>
    Task<Result<PagedResult<ContractDto>>> ListAsync(ContractQuery query, CancellationToken ct);

    /// <summary>Transiciona o status conforme a máquina de estados (R5.1/R5.2).</summary>
    Task<Result<ContractDto>> ChangeStatusAsync(Guid id, ChangeContractStatusRequest request, CancellationToken ct);

    /// <summary>
    /// Resolve o contrato aplicável a uma Impressora numa data, na ordem
    /// Impressora → Local → Cliente inteiro, considerando apenas contratos
    /// <c>Ativo</c> e vigentes (R4). Retorna <c>null</c> quando nenhum contrato
    /// se aplica — não é erro (R4.2).
    /// </summary>
    Task<Result<ContractDto?>> ResolveApplicableAsync(Guid printerId, DateTimeOffset referenceDate, CancellationToken ct);
}
