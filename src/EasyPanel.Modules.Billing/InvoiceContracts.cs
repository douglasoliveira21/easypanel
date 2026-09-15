using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Billing;

/// <summary>Projeção de leitura de um <see cref="InvoiceLineItem"/> (R4.3).</summary>
public sealed record InvoiceLineItemDto(
    Guid Id,
    Guid InvoiceId,
    Guid PrinterId,
    BillingCounterType CounterType,
    string? CounterTypeLabel,
    long ConsumedQuantity,
    long IncludedQuantity,
    long ExcessQuantity,
    decimal UnitPrice,
    decimal LineAmount);

/// <summary>Projeção de leitura de uma <see cref="Invoice"/> (R4, R5, R6.2), com seus Itens.</summary>
public sealed record InvoiceDto(
    Guid Id,
    Guid ContractId,
    Guid CustomerId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    InvoiceStatus Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<InvoiceLineItemDto> Items);

/// <summary>Filtro de listagem de <see cref="Invoice"/> (R6.1). Itens não são incluídos na listagem, só em <see cref="IInvoiceService.GetAsync"/>.</summary>
public sealed record InvoiceQuery(
    PageRequest Page,
    Guid? CustomerId = null,
    Guid? ContractId = null,
    InvoiceStatus? Status = null,
    int? Year = null,
    int? Month = null);

/// <summary>Dados de transição de status de uma <see cref="Invoice"/> (R5.2/R5.3).</summary>
public sealed record ChangeInvoiceStatusRequest(InvoiceStatus ToStatus);

/// <summary>Serviço de consulta e ciclo de vida de <see cref="Invoice"/> (R5/R6).</summary>
public interface IInvoiceService
{
    /// <summary>Consulta uma fatura por id, com seus Itens, restrita ao tenant (R6.2).</summary>
    Task<Result<InvoiceDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lista faturas com filtros/paginação, restrito ao tenant (R6.1).</summary>
    Task<Result<PagedResult<InvoiceDto>>> ListAsync(InvoiceQuery query, CancellationToken ct);

    /// <summary>Transiciona o status conforme a máquina de estados (R5.1–R5.4).</summary>
    Task<Result<InvoiceDto>> ChangeStatusAsync(Guid id, ChangeInvoiceStatusRequest request, CancellationToken ct);
}
