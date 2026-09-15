using EasyPanel.Modules.Alerting;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>Corpo da requisição de criação de um <see cref="AlertSilence"/> (R7.1/R7.2, R12.1).</summary>
public sealed record CreateAlertSilenceApiRequest(
    Guid? AlertRuleId,
    Guid? PrinterId,
    Guid? WindowsClientId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Reason);

/// <summary>Projeção de saída de um <see cref="AlertSilence"/> (R7, R12.1).</summary>
public sealed record AlertSilenceResponse(
    Guid Id,
    Guid TenantId,
    Guid? AlertRuleId,
    Guid? PrinterId,
    Guid? WindowsClientId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid CreatedByUserId,
    string? Reason,
    DateTimeOffset? EndedEarlyAt,
    Guid? EndedEarlyByUserId,
    DateTimeOffset CreatedAt);

/// <summary>Página de resultados da listagem de silenciamentos (R7.6, R12.4).</summary>
public sealed record AlertSilencePageResponse(
    IReadOnlyList<AlertSilenceResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
