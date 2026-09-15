using EasyPanel.Modules.Reporting;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>Linha de saída do relatório de faturamento (Fase 9 — R3).</summary>
public sealed record BillingReportRowResponse(Guid CustomerId, long InvoiceCount, decimal TotalAmount);

/// <summary>Resultado do relatório de faturamento (R3).</summary>
public sealed record BillingReportResponse(IReadOnlyList<BillingReportRowResponse> Rows, long TotalInvoiceCount, decimal GrandTotal);
