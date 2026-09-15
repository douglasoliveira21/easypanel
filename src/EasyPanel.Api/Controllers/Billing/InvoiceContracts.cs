using EasyPanel.Modules.Billing;

namespace EasyPanel.Api.Controllers.Billing;

/// <summary>Projeção de saída de um <see cref="InvoiceLineItem"/> (Fase 8 — R4.3).</summary>
public sealed record InvoiceLineItemResponse(
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

/// <summary>Projeção de saída de uma <see cref="Invoice"/> (R4, R5, R6.2), com seus Itens.</summary>
public sealed record InvoiceResponse(
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
    IReadOnlyList<InvoiceLineItemResponse> Items);

/// <summary>Página de resultados da listagem de faturas (R6.1, R12.4).</summary>
public sealed record InvoicePageResponse(IReadOnlyList<InvoiceResponse> Items, int Page, int PageSize, long TotalCount);

/// <summary>Corpo da requisição de transição de status de uma fatura (R5.2/R5.3).</summary>
public sealed record ChangeInvoiceStatusApiRequest(InvoiceStatus ToStatus);
