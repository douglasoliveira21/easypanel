using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Implementação de <see cref="IAlertRuleService"/> (R1). Como <see cref="AlertRule"/>
/// é uma <c>TenantEntity</c>, conta com o isolamento automático do ORM (filtro
/// global de consulta + interceptor de escrita), no mesmo padrão de
/// <c>LocationService</c>.
///
/// <para><b>Escopo (R1.3).</b> Quando <see cref="AlertRuleScopeType.Location"/>,
/// <see cref="AlertRuleScopeType.Printer"/> ou
/// <see cref="AlertRuleScopeType.WindowsClient"/>, o id correspondente é validado
/// contra o tenant corrente (a consulta já é auto-escopada pelo filtro global) —
/// um id inexistente ou de outro tenant é recusado como escopo inválido (R1.3),
/// sem revelar existência cross-tenant.</para>
/// </summary>
public sealed class AlertRuleService : IAlertRuleService
{
    private readonly AppDbContext _context;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;

    public AlertRuleService(AppDbContext context, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    {
        _context = context;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    /// <inheritdoc />
    public async Task<Result<AlertRuleDto>> CreateAsync(CreateAlertRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await ValidateAsync(
            request.EventTypes,
            request.ScopeType,
            request.ScopeLocationId,
            request.ScopePrinterId,
            request.ScopeWindowsClientId,
            request.ThresholdCount,
            request.ThresholdWindowMinutes,
            request.EmailEnabled,
            request.EmailRecipients,
            request.WebhookEnabled,
            request.WebhookUrl,
            request.WebhookSecret,
            ct)
            .ConfigureAwait(false);

        if (validation.IsFailure)
        {
            return Result.Failure<AlertRuleDto>(validation.Error);
        }

        var now = DateTimeOffset.UtcNow;
        var rule = new AlertRule
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description,
            IsActive = true,
            EventTypesCsv = ToCsv(request.EventTypes),
            ScopeType = request.ScopeType,
            ScopeLocationId = request.ScopeType == AlertRuleScopeType.Location ? request.ScopeLocationId : null,
            ScopePrinterId = request.ScopeType == AlertRuleScopeType.Printer ? request.ScopePrinterId : null,
            ScopeWindowsClientId = request.ScopeType == AlertRuleScopeType.WindowsClient
                ? request.ScopeWindowsClientId
                : null,
            Severity = request.Severity,
            ThresholdCount = request.ThresholdCount,
            ThresholdWindowMinutes = request.ThresholdWindowMinutes,
            AutoResolve = request.AutoResolve,
            EmailEnabled = request.EmailEnabled,
            EmailRecipientsCsv = request.EmailEnabled ? ToCsv(request.EmailRecipients!) : null,
            WebhookEnabled = request.WebhookEnabled,
            WebhookUrl = request.WebhookEnabled ? request.WebhookUrl : null,
            WebhookSecret = request.WebhookEnabled ? request.WebhookSecret : null,
            CreatedAt = now,
        };

        _context.Set<AlertRule>().Add(rule);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = rule.TenantId,
                Action = "alertrule.create",
                ResourceType = nameof(AlertRule),
                ResourceId = rule.Id.ToString(),
                NewValues = Summarize(rule),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(rule));
    }

    /// <inheritdoc />
    public async Task<Result<AlertRuleDto>> UpdateAsync(Guid id, UpdateAlertRuleRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rule = await _context.Set<AlertRule>().FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (rule is null)
        {
            return Result.Failure<AlertRuleDto>(AlertingErrors.NotFound);
        }

        var validation = await ValidateAsync(
            request.EventTypes,
            request.ScopeType,
            request.ScopeLocationId,
            request.ScopePrinterId,
            request.ScopeWindowsClientId,
            request.ThresholdCount,
            request.ThresholdWindowMinutes,
            request.EmailEnabled,
            request.EmailRecipients,
            request.WebhookEnabled,
            request.WebhookUrl,
            request.WebhookSecret,
            ct)
            .ConfigureAwait(false);

        if (validation.IsFailure)
        {
            return Result.Failure<AlertRuleDto>(validation.Error);
        }

        var before = Summarize(rule);

        rule.Name = request.Name.Trim();
        rule.Description = request.Description;
        rule.IsActive = request.IsActive;
        rule.EventTypesCsv = ToCsv(request.EventTypes);
        rule.ScopeType = request.ScopeType;
        rule.ScopeLocationId = request.ScopeType == AlertRuleScopeType.Location ? request.ScopeLocationId : null;
        rule.ScopePrinterId = request.ScopeType == AlertRuleScopeType.Printer ? request.ScopePrinterId : null;
        rule.ScopeWindowsClientId = request.ScopeType == AlertRuleScopeType.WindowsClient
            ? request.ScopeWindowsClientId
            : null;
        rule.Severity = request.Severity;
        rule.ThresholdCount = request.ThresholdCount;
        rule.ThresholdWindowMinutes = request.ThresholdWindowMinutes;
        rule.AutoResolve = request.AutoResolve;
        rule.EmailEnabled = request.EmailEnabled;
        rule.EmailRecipientsCsv = request.EmailEnabled ? ToCsv(request.EmailRecipients!) : null;
        rule.WebhookEnabled = request.WebhookEnabled;
        rule.WebhookUrl = request.WebhookEnabled ? request.WebhookUrl : null;
        rule.WebhookSecret = request.WebhookEnabled ? request.WebhookSecret : null;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = rule.TenantId,
                Action = "alertrule.update",
                ResourceType = nameof(AlertRule),
                ResourceId = rule.Id.ToString(),
                OldValues = before,
                NewValues = Summarize(rule),
                Result = AuditResult.Success,
            },
            ct)
            .ConfigureAwait(false);

        return Result.Success(MapToDto(rule));
    }

    /// <inheritdoc />
    public async Task<Result<AlertRuleDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var rule = await _context.Set<AlertRule>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);

        return rule is null
            ? Result.Failure<AlertRuleDto>(AlertingErrors.NotFound)
            : Result.Success(MapToDto(rule));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<AlertRuleDto>>> ListAsync(AlertRuleQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var baseQuery = _context.Set<AlertRule>().AsNoTracking();

        if (query.IsActive is { } isActive)
        {
            baseQuery = baseQuery.Where(r => r.IsActive == isActive);
        }

        if (query.ScopeType is { } scopeType)
        {
            baseQuery = baseQuery.Where(r => r.ScopeType == scopeType);
        }

        if (query.EventType is { } eventType)
        {
            var token = eventType.ToString(System.Globalization.CultureInfo.InvariantCulture);
            baseQuery = baseQuery.Where(r =>
                r.EventTypesCsv == token
                || r.EventTypesCsv.StartsWith(token + ",")
                || r.EventTypesCsv.EndsWith("," + token)
                || r.EventTypesCsv.Contains("," + token + ","));
        }

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page.Page - 1) * query.Page.PageSize;
        var rules = await baseQuery
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip(skip)
            .Take(query.Page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = rules.Select(MapToDto).ToList();
        return Result.Success(new PagedResult<AlertRuleDto>(items, query.Page.Page, query.Page.PageSize, totalCount));
    }

    private async Task<Result> ValidateAsync(
        IReadOnlyList<int> eventTypes,
        AlertRuleScopeType scopeType,
        Guid? scopeLocationId,
        Guid? scopePrinterId,
        Guid? scopeWindowsClientId,
        int? thresholdCount,
        int? thresholdWindowMinutes,
        bool emailEnabled,
        IReadOnlyList<string>? emailRecipients,
        bool webhookEnabled,
        string? webhookUrl,
        string? webhookSecret,
        CancellationToken ct)
    {
        if (eventTypes is null || eventTypes.Count == 0
            || eventTypes.Any(t => !Enum.IsDefined(typeof(PrinterEventType), t)))
        {
            return Result.Failure(AlertingErrors.EventTypesRequired);
        }

        if (thresholdCount.HasValue != thresholdWindowMinutes.HasValue)
        {
            return Result.Failure(AlertingErrors.InvalidThreshold);
        }

        if (thresholdCount is <= 0 || thresholdWindowMinutes is <= 0)
        {
            return Result.Failure(AlertingErrors.InvalidThreshold);
        }

        var scopeResult = await ValidateScopeAsync(scopeType, scopeLocationId, scopePrinterId, scopeWindowsClientId, ct)
            .ConfigureAwait(false);
        if (scopeResult.IsFailure)
        {
            return scopeResult;
        }

        if (emailEnabled && (emailRecipients is null || emailRecipients.Count == 0
            || emailRecipients.Any(string.IsNullOrWhiteSpace)))
        {
            return Result.Failure(AlertingErrors.EmailRecipientsRequired);
        }

        if (webhookEnabled)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(webhookSecret))
            {
                return Result.Failure(AlertingErrors.WebhookUrlRequired);
            }

            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return Result.Failure(AlertingErrors.WebhookUrlNotHttps);
            }
        }

        return Result.Success();
    }

    private async Task<Result> ValidateScopeAsync(
        AlertRuleScopeType scopeType,
        Guid? scopeLocationId,
        Guid? scopePrinterId,
        Guid? scopeWindowsClientId,
        CancellationToken ct)
    {
        switch (scopeType)
        {
            case AlertRuleScopeType.Tenant:
                return Result.Success();

            case AlertRuleScopeType.Location:
                if (scopeLocationId is not { } locationId
                    || !await _context.Set<EasyPanel.Modules.Customers.Location>()
                        .AsNoTracking()
                        .AnyAsync(l => l.Id == locationId, ct)
                        .ConfigureAwait(false))
                {
                    return Result.Failure(AlertingErrors.InvalidScope);
                }

                return Result.Success();

            case AlertRuleScopeType.Printer:
                if (scopePrinterId is not { } printerId
                    || !await _context.Set<Printer>()
                        .AsNoTracking()
                        .AnyAsync(p => p.Id == printerId, ct)
                        .ConfigureAwait(false))
                {
                    return Result.Failure(AlertingErrors.InvalidScope);
                }

                return Result.Success();

            case AlertRuleScopeType.WindowsClient:
                if (scopeWindowsClientId is not { } clientId
                    || !await _context.Set<WindowsClient>()
                        .AsNoTracking()
                        .AnyAsync(c => c.Id == clientId, ct)
                        .ConfigureAwait(false))
                {
                    return Result.Failure(AlertingErrors.InvalidScope);
                }

                return Result.Success();

            default:
                return Result.Failure(AlertingErrors.InvalidScope);
        }
    }

    private static string ToCsv(IReadOnlyList<int> values) =>
        string.Join(',', values.Distinct().OrderBy(v => v));

    private static string ToCsv(IReadOnlyList<string> values) =>
        string.Join(',', values.Select(v => v.Trim()));

    private static IReadOnlyList<int> ParseEventTypes(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    private static IReadOnlyList<string> ParseCsvList(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string Summarize(AlertRule r) =>
        $"Name={r.Name};IsActive={r.IsActive};Severity={r.Severity};ScopeType={r.ScopeType};" +
        $"EventTypes={r.EventTypesCsv};EmailEnabled={r.EmailEnabled};WebhookEnabled={r.WebhookEnabled}";

    private static AlertRuleDto MapToDto(AlertRule r) => new(
        r.Id,
        r.TenantId,
        r.Name,
        r.Description,
        r.IsActive,
        ParseEventTypes(r.EventTypesCsv),
        r.ScopeType,
        r.ScopeLocationId,
        r.ScopePrinterId,
        r.ScopeWindowsClientId,
        r.Severity,
        r.ThresholdCount,
        r.ThresholdWindowMinutes,
        r.AutoResolve,
        r.EmailEnabled,
        ParseCsvList(r.EmailRecipientsCsv),
        r.WebhookEnabled,
        r.WebhookUrl,
        r.CreatedAt,
        r.UpdatedAt);
}
