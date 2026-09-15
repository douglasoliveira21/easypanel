using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Impressora monitorável reportada ao agente (Fase 4): id no backend, endereço
/// e protocolo de coleta, para que o agente saiba o que consultar via SNMP e sob
/// qual <c>PrinterId</c> reportar.
/// </summary>
public sealed record MonitoredPrinter(
    Guid PrinterId,
    string Ip,
    string? Protocolo,
    int? Porta,
    string? Fabricante);

/// <summary>Configuração operacional entregue ao agente (R15.2).</summary>
public sealed record ClientConfig(
    int CollectionIntervalSeconds,
    IReadOnlyList<string> DiscoveryTargets,
    IReadOnlyList<string> IgnoredPrinters,
    IReadOnlyList<MonitoredPrinter>? MonitoredPrinters = null);

/// <summary>Serviço de configuração do agente (R15).</summary>
public interface IClientConfigService
{
    /// <summary>
    /// Retorna a configuração vigente do agente autenticado, restrita a tenant/local
    /// (R15.1). Resolvida da identidade do agente, nunca da requisição.
    /// </summary>
    Task<Result<ClientConfig>> GetAsync(CancellationToken ct);
}

/// <summary>Metadados de uma versão de atualização do agente (R16.1).</summary>
public sealed record ClientUpdateInfo(
    string Version,
    string PackageUrl,
    string Sha256,
    string Signature);

/// <summary>Serviço de atualização do agente (R16).</summary>
public interface IClientUpdateService
{
    /// <summary>Retorna os metadados da versão disponível para o agente (R16.1).</summary>
    Task<Result<ClientUpdateInfo>> GetLatestAsync(CancellationToken ct);
}

/// <summary>
/// Erros esperados do domínio de monitoramento, mapeados a HTTP por tipo (Fase 1).
/// </summary>
public static class MonitoringErrors
{
    /// <summary>Agente/registro sem credencial válida (R3.5/R4.3). → 401 na API.</summary>
    public static readonly Error Unauthorized = Error.Validation(
        "monitoring.unauthorized",
        "Credencial inválida ou expirada.");

    /// <summary>Recurso inexistente ou de outro tenant (R4.6/R9.4). → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "monitoring.not_found",
        "Recurso não encontrado.");

    /// <summary>Leitura de contador com decréscimo inválido (R11.5). → 409.</summary>
    public static readonly Error CounterDecrease = Error.Conflict(
        "monitoring.counter.decrease",
        "Leitura de contador inferior ao último valor registrado.");

    /// <summary>Local/cliente inválido para a operação (R9.3). → 400.</summary>
    public static readonly Error InvalidLocation = Error.Validation(
        "monitoring.location.invalid",
        "Local ou cliente informado é inválido para o tenant.");

    /// <summary>Justificativa ausente em ajuste administrativo (R11.6). → 400.</summary>
    public static readonly Error AdjustmentJustificationRequired = Error.Validation(
        "monitoring.counter.adjustment.justification_required",
        "O ajuste administrativo de contador exige justificativa.");
}
