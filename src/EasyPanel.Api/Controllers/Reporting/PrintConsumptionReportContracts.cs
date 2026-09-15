using EasyPanel.Modules.Reporting;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>Linha de saída do relatório de consumo de impressão (Fase 9 — R2).</summary>
public sealed record PrintConsumptionRowResponse(
    Guid CustomerId, Guid LocationId, Guid PrinterId, ReportingCounterType CounterType, long Consumed);

/// <summary>Resultado do relatório de consumo de impressão (R2).</summary>
public sealed record PrintConsumptionReportResponse(
    IReadOnlyList<PrintConsumptionRowResponse> Rows, DateTimeOffset StartDate, DateTimeOffset EndDate);
