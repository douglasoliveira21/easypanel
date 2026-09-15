using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Monitoring;

/// <summary>Projeção de leitura de uma <see cref="Printer"/> (R9/R12.1).</summary>
public sealed record PrinterDto(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    Guid LocationId,
    string? Fabricante,
    string? Modelo,
    string? NumeroSerie,
    string? Patrimonio,
    string? Ip,
    string? Mac,
    string? Hostname,
    string? Protocolo,
    int? Porta,
    PrinterStatus Status,
    bool MonitoringEnabled,
    DateTimeOffset? InstalledAt,
    string? Observacoes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Dados de criação de uma impressora (R9.3). Tenant vem do contexto.</summary>
public sealed record CreatePrinterRequest(
    Guid CustomerId,
    Guid LocationId,
    string? Fabricante = null,
    string? Modelo = null,
    string? NumeroSerie = null,
    string? Patrimonio = null,
    string? Ip = null,
    string? Mac = null,
    string? Hostname = null,
    string? Protocolo = null,
    int? Porta = null,
    bool MonitoringEnabled = true,
    string? Observacoes = null);

/// <summary>Dados de atualização de uma impressora (R9).</summary>
public sealed record UpdatePrinterRequest(
    string? Fabricante,
    string? Modelo,
    string? NumeroSerie,
    string? Patrimonio,
    string? Ip,
    string? Mac,
    string? Hostname,
    string? Protocolo,
    int? Porta,
    bool MonitoringEnabled,
    string? Observacoes);

/// <summary>Filtro de listagem do parque de impressoras (R9.5).</summary>
public sealed record PrinterQuery(
    PageRequest Page,
    Guid? CustomerId = null,
    Guid? LocationId = null,
    string? Fabricante = null,
    string? Modelo = null,
    PrinterStatus? Status = null,
    bool? MonitoringEnabled = null);

/// <summary>Serviço de cadastro, consulta e ciclo de vida de impressoras (R9/R10).</summary>
public interface IPrinterService
{
    /// <summary>Cria uma impressora vinculada ao tenant do contexto (R9.3).</summary>
    Task<Result<PrinterDto>> CreateAsync(CreatePrinterRequest request, CancellationToken ct);

    /// <summary>Atualiza uma impressora do próprio tenant (R9); cross-tenant → 404.</summary>
    Task<Result<PrinterDto>> UpdateAsync(Guid id, UpdatePrinterRequest request, CancellationToken ct);

    /// <summary>Consulta por id, restrita ao tenant (R9.4).</summary>
    Task<Result<PrinterDto>> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lista o parque com filtros/paginação, restrito ao tenant (R9.5).</summary>
    Task<Result<PagedResult<PrinterDto>>> ListAsync(PrinterQuery query, CancellationToken ct);

    /// <summary>Instala/transfere/recolhe/desabilita/reativa, registrando histórico (R10).</summary>
    Task<Result<PrinterDto>> MoveAsync(Guid id, MovementOperation operation, Guid? toLocationId, CancellationToken ct);
}

/// <summary>Projeção de leitura de uma leitura de contador (R11).</summary>
public sealed record PrinterCounterDto(
    Guid Id,
    Guid PrinterId,
    DateTimeOffset Timestamp,
    CounterType CounterType,
    string? CounterTypeLabel,
    long Value,
    CounterSource Source,
    bool IsAdministrativeAdjustment);

/// <summary>Ajuste administrativo de contador (R11.6).</summary>
public sealed record CounterAdjustmentRequest(
    Guid PrinterId,
    CounterType CounterType,
    string? CounterTypeLabel,
    long NewValue,
    string Justification);

/// <summary>Página baseada em cursor para grandes volumes (R11.7).</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Serviço de contadores (R11).</summary>
public interface ICounterService
{
    /// <summary>
    /// Registra uma leitura de contador aplicando a validação de não-decréscimo por
    /// (impressora, tipo) (R11.5). Retorna falha se for decréscimo inválido.
    /// </summary>
    Task<Result<PrinterCounterDto>> RecordAsync(
        Guid printerId,
        CounterType counterType,
        string? counterTypeLabel,
        long value,
        CounterSource source,
        Guid? windowsClientId,
        Guid? collectionId,
        CancellationToken ct);

    /// <summary>Aplica um ajuste administrativo auditado que pode reduzir o valor (R11.6).</summary>
    Task<Result<PrinterCounterDto>> AdjustAsync(CounterAdjustmentRequest request, CancellationToken ct);

    /// <summary>Consulta o histórico por cursor pagination, restrito ao tenant (R11.7).</summary>
    Task<Result<CursorPage<PrinterCounterDto>>> ListAsync(
        Guid printerId,
        CounterType? counterType,
        string? cursor,
        int pageSize,
        CancellationToken ct);
}
