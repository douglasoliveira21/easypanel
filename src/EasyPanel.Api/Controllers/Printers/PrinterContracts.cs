using EasyPanel.Modules.Monitoring;

namespace EasyPanel.Api.Controllers.Printers;

/// <summary>Corpo de criação de impressora na API (R9.3).</summary>
public sealed record CreatePrinterApiRequest(
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
    bool MonitoringEnabled = true,
    string? Observacoes = null);

/// <summary>Corpo de atualização de impressora na API (R9).</summary>
public sealed record UpdatePrinterApiRequest(
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

/// <summary>Corpo de movimentação/ciclo de vida (R10).</summary>
public sealed record MovePrinterApiRequest(MovementOperation Operation, Guid? ToLocationId);

/// <summary>Projeção de leitura de impressora na API (R9).</summary>
public sealed record PrinterResponse(
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

/// <summary>Página de impressoras na API.</summary>
public sealed record PrinterPageResponse(
    IReadOnlyList<PrinterResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
