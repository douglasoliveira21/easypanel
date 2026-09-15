using EasyPanel.Modules.Alerting;

namespace EasyPanel.Api.Controllers.Alerting;

/// <summary>
/// Corpo da requisição de criação de uma <see cref="AlertRule"/> na camada de API
/// (R1.2, R12.1). Contrato distinto do <see cref="CreateAlertRuleRequest"/> de
/// domínio; o tenant não é informado aqui — vem do contexto autenticado (R6.3).
/// </summary>
public sealed record CreateAlertRuleApiRequest(
    string Name,
    string? Description,
    IReadOnlyList<int> EventTypes,
    AlertRuleScopeType ScopeType,
    Guid? ScopeLocationId,
    Guid? ScopePrinterId,
    Guid? ScopeWindowsClientId,
    AlertSeverity Severity,
    int? ThresholdCount,
    int? ThresholdWindowMinutes,
    bool AutoResolve,
    bool EmailEnabled,
    IReadOnlyList<string>? EmailRecipients,
    bool WebhookEnabled,
    string? WebhookUrl,
    string? WebhookSecret);

/// <summary>Corpo da requisição de atualização de uma <see cref="AlertRule"/> (R1, R12.1).</summary>
public sealed record UpdateAlertRuleApiRequest(
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<int> EventTypes,
    AlertRuleScopeType ScopeType,
    Guid? ScopeLocationId,
    Guid? ScopePrinterId,
    Guid? ScopeWindowsClientId,
    AlertSeverity Severity,
    int? ThresholdCount,
    int? ThresholdWindowMinutes,
    bool AutoResolve,
    bool EmailEnabled,
    IReadOnlyList<string>? EmailRecipients,
    bool WebhookEnabled,
    string? WebhookUrl,
    string? WebhookSecret);

/// <summary>
/// Projeção de saída de uma <see cref="AlertRule"/> na camada de API (R1.6,
/// R12.1). Nunca inclui <see cref="AlertRule.WebhookSecret"/> (R5.6).
/// </summary>
public sealed record AlertRuleResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    bool IsActive,
    IReadOnlyList<int> EventTypes,
    AlertRuleScopeType ScopeType,
    Guid? ScopeLocationId,
    Guid? ScopePrinterId,
    Guid? ScopeWindowsClientId,
    AlertSeverity Severity,
    int? ThresholdCount,
    int? ThresholdWindowMinutes,
    bool AutoResolve,
    bool EmailEnabled,
    IReadOnlyList<string> EmailRecipients,
    bool WebhookEnabled,
    string? WebhookUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Página de resultados da listagem de regras de alerta (R1.6, R12.4).</summary>
public sealed record AlertRulePageResponse(
    IReadOnlyList<AlertRuleResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
