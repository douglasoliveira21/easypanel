using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>
/// Painel de visão geral do tenant do contexto (Fase 9 — R1). Exige
/// <c>relatorio.view</c>.
/// </summary>
[ApiController]
[Route("api/v1/reports/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    private readonly IDashboardService _dashboardService = dashboardService;

    /// <summary>Calcula o painel a partir do estado corrente dos dados (R1.1/R1.2).</summary>
    [HttpGet]
    [RequirePermission(Permissions.RelatorioView)]
    [ProducesResponseType(typeof(DashboardOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
        var overview = result.Value;

        return Ok(new DashboardOverviewResponse(
            overview.PrintersByStatus,
            overview.AlertsByState,
            overview.OpenAlertsBySeverity,
            overview.TicketsByStatus,
            overview.LowStockItemCount,
            overview.InvoicesByStatus,
            overview.InvoicesPendingTotalAmount,
            overview.GeneratedAt));
    }
}
