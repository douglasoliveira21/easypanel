using EasyPanel.Modules.Monitoring;

namespace EasyPanel.Api.Controllers.Supplies;

/// <summary>Nível atual de um rótulo de suprimento, com previsão de troca (Fase 4 — R3.4/R5).</summary>
public sealed record SupplyLevelResponse(
    Guid PrinterId,
    string Label,
    int Percent,
    DateTimeOffset Timestamp,
    DateTimeOffset? ForecastDepletionAt);

/// <summary>Projeção de leitura de histórico de suprimento (Fase 4 — R3.3).</summary>
public sealed record SupplyReadingResponse(Guid Id, Guid PrinterId, string Label, int Percent, DateTimeOffset Timestamp);

/// <summary>Página baseada em cursor para o histórico de suprimento.</summary>
public sealed record SupplyCursorPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de leitura de um limiar de suprimento (Fase 4 — R4).</summary>
public sealed record SupplyThresholdResponse(Guid Id, Guid? PrinterId, string? Label, int ThresholdPercent);

/// <summary>Corpo da requisição de upsert de um limiar de suprimento (Fase 4 — R4.2).</summary>
public sealed record SetSupplyThresholdApiRequest(Guid? PrinterId, string? Label, int ThresholdPercent);

/// <summary>Página de resultados da listagem de limiares (R4, R6.4).</summary>
public sealed record SupplyThresholdPageResponse(
    IReadOnlyList<SupplyThresholdResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
