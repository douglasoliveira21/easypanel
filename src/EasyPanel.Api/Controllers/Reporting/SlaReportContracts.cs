namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>Distribuição Cumprido/Violado/Pendente com taxa de cumprimento (Fase 9 — R4.2).</summary>
public sealed record SlaComplianceBreakdownResponse(long Cumprido, long Violado, long Pendente, decimal ComplianceRate);

/// <summary>Resultado do relatório de cumprimento de SLA (R4).</summary>
public sealed record SlaReportResponse(long TotalTickets, SlaComplianceBreakdownResponse FirstResponse, SlaComplianceBreakdownResponse Resolution);
