using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Reporting;

/// <summary>Filtro do relatório de faturamento (Fase 9 — R3).</summary>
public sealed record BillingReportQuery(
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    ReportingInvoiceStatus? Status = null);

/// <summary>Linha do relatório de faturamento: total faturado de um Cliente no intervalo (R3.1).</summary>
public sealed record BillingReportRowDto(Guid CustomerId, long InvoiceCount, decimal TotalAmount);

/// <summary>Resultado do relatório de faturamento (R3).</summary>
public sealed record BillingReportDto(IReadOnlyList<BillingReportRowDto> Rows, long TotalInvoiceCount, decimal GrandTotal);

/// <summary>Serviço do relatório de faturamento por período (R3).</summary>
public interface IBillingReportService
{
    /// <summary>
    /// Agrupa por Cliente as Faturas cujo período se sobrepõe ao intervalo
    /// solicitado (R3.1), com filtro opcional por status (R3.2). Intervalo
    /// inválido ou maior que o limite → 400 (R6.5).
    /// </summary>
    Task<Result<BillingReportDto>> GetAsync(BillingReportQuery query, CancellationToken ct);
}
