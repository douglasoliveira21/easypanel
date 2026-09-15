using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Alerting;

/// <summary>Página baseada em cursor para grandes volumes (R6.2), específica deste módulo
/// para não referenciar <c>Modules.Monitoring.CursorPage&lt;T&gt;</c> entre módulos.</summary>
public sealed record AlertCursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Projeção de leitura de uma <see cref="AlertTransition"/> (R3.7).</summary>
public sealed record AlertTransitionDto(
    Guid Id,
    AlertState FromState,
    AlertState ToState,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string? Note);

/// <summary>Projeção de leitura de um <see cref="Alert"/> (R3.1).</summary>
public sealed record AlertDto(
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
    IReadOnlyList<AlertTransitionDto>? Transitions = null);

/// <summary>Projeção de leitura de uma <see cref="AlertNotificationAttempt"/> (R6).</summary>
public sealed record AlertNotificationAttemptDto(
    Guid Id,
    Guid AlertId,
    NotificationChannel Channel,
    NotificationOutcome Outcome,
    int AttemptNumber,
    int? HttpStatusCode,
    string? ErrorSummary,
    DateTimeOffset AttemptedAt);

/// <summary>Filtro de listagem de <see cref="Alert"/> por cursor (R3.6).</summary>
public sealed record AlertQuery(
    AlertState? State = null,
    AlertSeverity? Severity = null,
    Guid? AlertRuleId = null,
    Guid? PrinterId = null,
    Guid? WindowsClientId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Cursor = null,
    int PageSize = 50);

/// <summary>Dados de resolução manual de um Alerta (R3.4).</summary>
public sealed record ResolveAlertRequest(string? Note);

/// <summary>Serviço de consulta e ciclo de vida de <see cref="Alert"/> (R3/R6).</summary>
public interface IAlertService
{
    /// <summary>Lista Alertas por cursor pagination, restrito ao tenant (R3.6).</summary>
    Task<Result<AlertCursorPage<AlertDto>>> ListAsync(AlertQuery query, CancellationToken ct);

    /// <summary>Consulta um Alerta por id, incluindo seu histórico de transições (R3.7).</summary>
    Task<Result<AlertDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Reconhece um Alerta em aberto do próprio tenant (R3.3).</summary>
    Task<Result<AlertDto>> AcknowledgeAsync(Guid id, CancellationToken ct);

    /// <summary>Resolve manualmente um Alerta do próprio tenant (R3.4).</summary>
    Task<Result<AlertDto>> ResolveAsync(Guid id, ResolveAlertRequest request, CancellationToken ct);

    /// <summary>Lista o histórico de notificações de um Alerta por cursor pagination (R6.2).</summary>
    Task<Result<AlertCursorPage<AlertNotificationAttemptDto>>> ListNotificationsAsync(
        Guid alertId,
        string? cursor,
        int pageSize,
        CancellationToken ct);
}
