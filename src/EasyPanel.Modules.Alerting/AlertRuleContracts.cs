using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Projeção de leitura de uma <see cref="AlertRule"/> (R1). Nunca inclui
/// <see cref="AlertRule.WebhookSecret"/> (R5.6); <see cref="EventTypes"/> expõe os
/// valores inteiros de <c>PrinterEventType</c> (Modules.Monitoring), sem referenciar
/// o tipo diretamente para preservar o isolamento entre módulos.
/// </summary>
public sealed record AlertRuleDto(
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

/// <summary>Dados de criação de uma <see cref="AlertRule"/> (R1.2). Tenant vem do contexto.</summary>
public sealed record CreateAlertRuleRequest(
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

/// <summary>Dados de atualização de uma <see cref="AlertRule"/> (R1).</summary>
public sealed record UpdateAlertRuleRequest(
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

/// <summary>Filtro de listagem de <see cref="AlertRule"/> (R1.6).</summary>
public sealed record AlertRuleQuery(
    PageRequest Page,
    bool? IsActive = null,
    AlertRuleScopeType? ScopeType = null,
    int? EventType = null);

/// <summary>Serviço de CRUD e validação de <see cref="AlertRule"/> (R1).</summary>
public interface IAlertRuleService
{
    /// <summary>Cria uma regra vinculada ao tenant do contexto (R1.2).</summary>
    Task<Result<AlertRuleDto>> CreateAsync(CreateAlertRuleRequest request, CancellationToken ct);

    /// <summary>Atualiza uma regra do próprio tenant (R1); cross-tenant → 404.</summary>
    Task<Result<AlertRuleDto>> UpdateAsync(Guid id, UpdateAlertRuleRequest request, CancellationToken ct);

    /// <summary>Consulta por id, restrita ao tenant (R1.6).</summary>
    Task<Result<AlertRuleDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lista regras com filtros/paginação, restrito ao tenant (R1.6).</summary>
    Task<Result<PagedResult<AlertRuleDto>>> ListAsync(AlertRuleQuery query, CancellationToken ct);
}
