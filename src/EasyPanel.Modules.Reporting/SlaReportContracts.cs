using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Reporting;

/// <summary>Filtro do relatório de cumprimento de SLA (Fase 9 — R4).</summary>
public sealed record SlaReportQuery(DateTimeOffset StartDate, DateTimeOffset EndDate);

/// <summary>Distribuição Cumprido/Violado/Pendente de um indicador de SLA, com a taxa de cumprimento (R4.2).</summary>
public sealed record SlaComplianceBreakdownDto(long Cumprido, long Violado, long Pendente, decimal ComplianceRate);

/// <summary>Resultado do relatório de cumprimento de SLA (R4).</summary>
public sealed record SlaReportDto(long TotalTickets, SlaComplianceBreakdownDto FirstResponse, SlaComplianceBreakdownDto Resolution);

/// <summary>Serviço do relatório de cumprimento de SLA por período de abertura de chamado (R4).</summary>
public interface ISlaReportService
{
    /// <summary>
    /// Agrega os Chamados abertos no intervalo por cumprimento de SLA de
    /// primeira resposta e de resolução (R4.1/R4.2). Intervalo sem
    /// chamados retorna contagens/taxas zeradas, não erro (R4.3).
    /// Intervalo inválido ou maior que o limite → 400 (R6.5).
    /// </summary>
    Task<Result<SlaReportDto>> GetAsync(SlaReportQuery query, CancellationToken ct);
}
