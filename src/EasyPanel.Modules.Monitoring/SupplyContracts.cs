using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Monitoring;

/// <summary>Projeção de leitura de um <see cref="SupplyReading"/> (histórico — R3).</summary>
public sealed record SupplyReadingDto(
    Guid Id,
    Guid PrinterId,
    string Label,
    int Percent,
    DateTimeOffset Timestamp);

/// <summary>
/// Nível atual de um rótulo de suprimento de uma impressora, com previsão de troca
/// quando calculável (R5).
/// </summary>
public sealed record SupplyLevelDto(
    Guid PrinterId,
    string Label,
    int Percent,
    DateTimeOffset Timestamp,
    DateTimeOffset? ForecastDepletionAt);

/// <summary>Projeção de leitura de um <see cref="SupplyThreshold"/> (R4).</summary>
public sealed record SupplyThresholdDto(
    Guid Id,
    Guid? PrinterId,
    string? Label,
    int ThresholdPercent);

/// <summary>Dados para criar/atualizar (upsert) um limiar de suprimento (R4.2).</summary>
public sealed record SetSupplyThresholdRequest(Guid? PrinterId, string? Label, int ThresholdPercent);

/// <summary>Filtro de listagem de limiares configurados (R4).</summary>
public sealed record SupplyThresholdQuery(PageRequest Page);

/// <summary>Serviço de consulta de níveis/histórico/previsão e gestão de limiares de suprimento (Fase 4).</summary>
public interface ISupplyService
{
    /// <summary>Níveis atuais (última leitura de cada rótulo) de uma Impressora, com previsão de troca (R3.4/R5).</summary>
    Task<Result<IReadOnlyList<SupplyLevelDto>>> GetCurrentLevelsAsync(Guid printerId, CancellationToken ct);

    /// <summary>Histórico de leituras de uma Impressora por cursor pagination, com filtro opcional por rótulo (R3.3).</summary>
    Task<Result<CursorPage<SupplyReadingDto>>> ListHistoryAsync(
        Guid printerId,
        string? label,
        string? cursor,
        int pageSize,
        CancellationToken ct);

    /// <summary>Lista os limiares configurados no tenant, com paginação (R4).</summary>
    Task<Result<PagedResult<SupplyThresholdDto>>> ListThresholdsAsync(SupplyThresholdQuery query, CancellationToken ct);

    /// <summary>Cria ou atualiza (upsert por Impressora+Rótulo) um limiar de suprimento (R4.2), auditado.</summary>
    Task<Result<SupplyThresholdDto>> SetThresholdAsync(SetSupplyThresholdRequest request, CancellationToken ct);

    /// <summary>Remove um limiar específico, voltando à cascata de resolução, auditado.</summary>
    Task<Result> DeleteThresholdAsync(Guid id, CancellationToken ct);
}

/// <summary>Erros de domínio de suprimento (Fase 4), no padrão de <see cref="MonitoringErrors"/>.</summary>
public static class SupplyErrors
{
    /// <summary>Recurso inexistente ou de outro tenant. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "supply.not_found",
        "Recurso não encontrado.");

    /// <summary>Percentual de limiar fora do intervalo 0–100. → 400.</summary>
    public static readonly Error InvalidThresholdPercent = Error.Validation(
        "supply.threshold.invalid_percent",
        "O limiar deve estar entre 0 e 100.");
}
