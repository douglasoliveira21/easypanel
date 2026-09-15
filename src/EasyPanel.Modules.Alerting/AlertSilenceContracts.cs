using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Alerting;

/// <summary>Projeção de leitura de um <see cref="AlertSilence"/> (R7).</summary>
public sealed record AlertSilenceDto(
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

/// <summary>Dados de criação de um <see cref="AlertSilence"/> (R7.1/R7.2). Tenant e ator vêm do contexto.</summary>
public sealed record CreateAlertSilenceRequest(
    Guid? AlertRuleId,
    Guid? PrinterId,
    Guid? WindowsClientId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Reason);

/// <summary>Filtro de listagem de <see cref="AlertSilence"/> (R7.6).</summary>
public sealed record AlertSilenceQuery(PageRequest Page, bool? ActiveOnly = null);

/// <summary>Serviço de CRUD de <see cref="AlertSilence"/> (R7).</summary>
public interface IAlertSilenceService
{
    /// <summary>Cria um silenciamento vinculado ao tenant do contexto (R7.2).</summary>
    Task<Result<AlertSilenceDto>> CreateAsync(CreateAlertSilenceRequest request, CancellationToken ct);

    /// <summary>Lista silenciamentos com filtros/paginação, restrito ao tenant (R7.6).</summary>
    Task<Result<PagedResult<AlertSilenceDto>>> ListAsync(AlertSilenceQuery query, CancellationToken ct);

    /// <summary>Encerra antecipadamente um silenciamento do próprio tenant (R7.5).</summary>
    Task<Result<AlertSilenceDto>> EndEarlyAsync(Guid id, CancellationToken ct);
}
