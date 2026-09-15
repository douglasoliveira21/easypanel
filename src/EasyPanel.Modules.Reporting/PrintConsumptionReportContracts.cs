using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Reporting;

/// <summary>Filtro do relatório de consumo de impressão (Fase 9 — R2).</summary>
public sealed record PrintConsumptionReportQuery(
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    Guid? CustomerId = null,
    Guid? LocationId = null);

/// <summary>Linha do relatório de consumo: consumo de um (Cliente, Local, Impressora, CounterType) no intervalo (R2.1/R2.2).</summary>
public sealed record PrintConsumptionRowDto(
    Guid CustomerId,
    Guid LocationId,
    Guid PrinterId,
    ReportingCounterType CounterType,
    long Consumed);

/// <summary>Resultado do relatório de consumo de impressão (R2).</summary>
public sealed record PrintConsumptionReportDto(
    IReadOnlyList<PrintConsumptionRowDto> Rows,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);

/// <summary>Serviço do relatório de consumo de impressão por período (R2).</summary>
public interface IPrintConsumptionReportService
{
    /// <summary>
    /// Calcula o consumo por (Impressora, CounterType) no intervalo, como a
    /// leitura de referência de fim menos a de início (R2.1, mesma técnica
    /// da Fase 8), agrupado por Cliente/Local (R2.2). Intervalo inválido ou
    /// maior que o limite → 400 (R2.3, R6.5).
    /// </summary>
    Task<Result<PrintConsumptionReportDto>> GetAsync(PrintConsumptionReportQuery query, CancellationToken ct);
}
