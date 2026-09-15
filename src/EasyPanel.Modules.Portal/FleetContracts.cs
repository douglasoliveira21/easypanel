using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Portal;

/// <summary>
/// Impressora exposta pelo Portal do Cliente (Fase 10 — R3.1), restrita ao
/// Cliente do Escopo de Cliente. Projeção somente-leitura, sem campos internos
/// de operação (IP/MAC/protocolo/patrimônio), que não são pertinentes à visão
/// do Cliente.
/// </summary>
public sealed record PortalPrinterDto(
    Guid Id,
    string? Fabricante,
    string? Modelo,
    string? NumeroSerie,
    Guid LocationId,
    string LocationName,
    PortalPrinterStatus Status);

/// <summary>
/// Leitura de contador exposta pelo Portal do Cliente (Fase 10 — R3.1),
/// restrita a uma Impressora do próprio Cliente.
/// </summary>
public sealed record PortalCounterReadingDto(
    DateTimeOffset Timestamp, string CounterType, long Value);

/// <summary>
/// Leitura/histórico do parque de Impressoras e contadores do Cliente do
/// Escopo de Cliente (Fase 10 — R3). Toda consulta é restrita ao Cliente
/// vinculado ao usuário autenticado, além do tenant (isolamento herdado).
/// </summary>
public interface IPortalFleetService
{
    /// <summary>
    /// Lista as Impressoras do Cliente do Escopo de Cliente, por cursor
    /// (R3.1).
    /// </summary>
    Task<Result<PortalCursorPage<PortalPrinterDto>>> ListPrintersAsync(
        string? cursor, int pageSize, CancellationToken ct);

    /// <summary>
    /// Lista o histórico de contadores de uma Impressora, por cursor. Retorna
    /// <see cref="PortalErrors.NotFound"/> se a Impressora não pertencer ao
    /// Cliente do Escopo de Cliente (R3.2).
    /// </summary>
    Task<Result<PortalCursorPage<PortalCounterReadingDto>>> GetCounterHistoryAsync(
        Guid printerId, string? cursor, int pageSize, CancellationToken ct);
}
