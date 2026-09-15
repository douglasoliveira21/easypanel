using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Ticketing;

/// <summary>Projeção de leitura de uma <see cref="SlaPolicy"/> (R4).</summary>
public sealed record SlaPolicyDto(
    Guid Id,
    TicketPriority Priority,
    int FirstResponseMinutes,
    int ResolutionMinutes);

/// <summary>Dados de upsert (por prioridade) de uma <see cref="SlaPolicy"/> (R4.1). Tenant vem do contexto.</summary>
public sealed record SetSlaPolicyRequest(
    TicketPriority Priority,
    int FirstResponseMinutes,
    int ResolutionMinutes);

/// <summary>Serviço de política de SLA por tenant/prioridade (R4.1/R4.2/R4.7).</summary>
public interface ISlaPolicyService
{
    /// <summary>Lista as políticas configuradas do tenant do contexto.</summary>
    Task<Result<IReadOnlyList<SlaPolicyDto>>> ListAsync(CancellationToken ct);

    /// <summary>Cria ou atualiza (upsert por Prioridade) uma política de SLA, auditado (R4.7).</summary>
    Task<Result<SlaPolicyDto>> SetAsync(SetSlaPolicyRequest request, CancellationToken ct);
}
