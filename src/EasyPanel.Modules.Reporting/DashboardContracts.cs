using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Reporting;

/// <summary>
/// Painel de visão geral do tenant (Fase 9 — R1): indicadores de estado
/// corrente, calculados ao vivo a cada consulta. Todo dicionário traz todas
/// as chaves possíveis do enum correspondente, mesmo com contagem zero
/// (R1.3).
/// </summary>
public sealed record DashboardOverviewDto(
    IReadOnlyDictionary<string, long> PrintersByStatus,
    IReadOnlyDictionary<string, long> AlertsByState,
    IReadOnlyDictionary<string, long> OpenAlertsBySeverity,
    IReadOnlyDictionary<string, long> TicketsByStatus,
    long LowStockItemCount,
    IReadOnlyDictionary<string, long> InvoicesByStatus,
    decimal InvoicesPendingTotalAmount,
    DateTimeOffset GeneratedAt);

/// <summary>Serviço do painel de visão geral (R1).</summary>
public interface IDashboardService
{
    /// <summary>Calcula o painel a partir do estado corrente dos dados, restrito ao tenant do contexto (R1.1/R1.2).</summary>
    Task<Result<DashboardOverviewDto>> GetOverviewAsync(CancellationToken ct);
}
