using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Portal;

/// <summary>
/// Resumo de Fatura exposto pelo Portal do Cliente (Fase 10 — R5.1), restrito
/// ao Cliente do Escopo de Cliente. Nunca inclui Faturas em
/// <see cref="PortalInvoiceStatus.Rascunho"/> (R5.2).
/// </summary>
public sealed record PortalInvoiceSummaryDto(
    Guid Id,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    PortalInvoiceStatus Status,
    decimal TotalAmount);

/// <summary>
/// Item de uma Fatura exposto pelo Portal do Cliente (Fase 10 — R5.3), por
/// Impressora e tipo de contador.
/// </summary>
public sealed record PortalInvoiceLineItemDto(
    Guid PrinterId,
    string CounterType,
    long ConsumedQuantity,
    long IncludedQuantity,
    long ExcessQuantity,
    decimal UnitPrice,
    decimal LineAmount);

/// <summary>
/// Detalhe de Fatura exposto pelo Portal do Cliente (Fase 10 — R5.3), com os
/// itens de excedente.
/// </summary>
public sealed record PortalInvoiceDetailDto(
    Guid Id,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    PortalInvoiceStatus Status,
    decimal TotalAmount,
    IReadOnlyList<PortalInvoiceLineItemDto> LineItems);

/// <summary>
/// Leitura de Faturas do Cliente do Escopo de Cliente (Fase 10 — R5), excluindo
/// sempre as Faturas em Rascunho (R5.2).
/// </summary>
public interface IPortalInvoiceService
{
    /// <summary>Lista as Faturas emitidas/canceladas do Cliente do Escopo de Cliente, por cursor (R5.1).</summary>
    Task<Result<PortalCursorPage<PortalInvoiceSummaryDto>>> ListAsync(
        string? cursor, int pageSize, CancellationToken ct);

    /// <summary>
    /// Detalha uma Fatura, com itens de excedente. Retorna
    /// <see cref="PortalErrors.NotFound"/> se não pertencer ao Cliente do Escopo
    /// de Cliente ou estiver em Rascunho (R5.3).
    /// </summary>
    Task<Result<PortalInvoiceDetailDto>> GetAsync(Guid invoiceId, CancellationToken ct);
}
