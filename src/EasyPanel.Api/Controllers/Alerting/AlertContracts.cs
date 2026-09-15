using EasyPanel.Modules.Alerting;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>Página baseada em cursor para grandes volumes na camada de API (R3.6/R6.2).</summary>
public sealed record AlertCursorPageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de saída de uma <see cref="AlertTransition"/> (R3.7).</summary>
public sealed record AlertTransitionResponse(
    Guid Id,
    AlertState FromState,
    AlertState ToState,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string? Note);

/// <summary>Projeção de saída de um <see cref="Alert"/> (R3.1, R12.1).</summary>
public sealed record AlertResponse(
    Guid Id,
    Guid TenantId,
    Guid AlertRuleId,
    AlertSeverity Severity,
    AlertState State,
    Guid? PrinterId,
    Guid? WindowsClientId,
    DateTimeOffset FirstOccurrenceAt,
    DateTimeOffset LastOccurrenceAt,
    int OccurrenceCount,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedByUserId,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedByUserId,
    bool AutoResolved,
    string? ResolutionNote,
    IReadOnlyList<AlertTransitionResponse>? Transitions);

/// <summary>Projeção de saída de uma <see cref="AlertNotificationAttempt"/> (R6).</summary>
public sealed record AlertNotificationAttemptResponse(
    Guid Id,
    Guid AlertId,
    NotificationChannel Channel,
    NotificationOutcome Outcome,
    int AttemptNumber,
    int? HttpStatusCode,
    string? ErrorSummary,
    DateTimeOffset AttemptedAt);

/// <summary>Corpo da requisição de resolução manual de um Alerta (R3.4, R12.1).</summary>
public sealed record ResolveAlertApiRequest(string? Note);
