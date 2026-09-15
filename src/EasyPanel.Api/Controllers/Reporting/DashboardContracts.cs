namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>Projeção de saída do painel de visão geral (Fase 9 — R1).</summary>
public sealed record DashboardOverviewResponse(
    IReadOnlyDictionary<string, long> PrintersByStatus,
    IReadOnlyDictionary<string, long> AlertsByState,
    IReadOnlyDictionary<string, long> OpenAlertsBySeverity,
    IReadOnlyDictionary<string, long> TicketsByStatus,
    long LowStockItemCount,
    IReadOnlyDictionary<string, long> InvoicesByStatus,
    decimal InvoicesPendingTotalAmount,
    DateTimeOffset GeneratedAt);
