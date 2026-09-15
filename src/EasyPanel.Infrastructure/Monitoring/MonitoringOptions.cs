using System.ComponentModel.DataAnnotations;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Parâmetros operacionais do monitoramento (Fase 2), vinculados à seção
/// <c>Monitoring</c> da configuração.
/// </summary>
public sealed class MonitoringOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Monitoring";

    /// <summary>
    /// Limite (em segundos) sem heartbeat após o qual um agente é marcado como
    /// <c>HeartbeatMissing</c> e um evento é gerado (R6.4).
    /// </summary>
    [Range(30, 86_400)]
    public int HeartbeatMissingThresholdSeconds { get; set; } = 300;

    /// <summary>Intervalo (em segundos) entre varreduras do <c>HeartbeatMonitor</c>.</summary>
    [Range(10, 3_600)]
    public int HeartbeatScanIntervalSeconds { get; set; } = 60;

    /// <summary>Intervalo de coleta padrão entregue aos agentes na config (R15.2).</summary>
    [Range(30, 86_400)]
    public int DefaultCollectionIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Limiar percentual de suprimento padrão de plataforma (Fase 4 — R4.1), usado
    /// quando o tenant não configurou nenhum <c>SupplyThreshold</c> (nem geral, nem
    /// por rótulo, nem por impressora).
    /// </summary>
    [Range(0, 100)]
    public int DefaultSupplyThresholdPercent { get; set; } = 10;

    /// <summary>Metadados da versão de atualização disponível do agente (R16.1).</summary>
    public ClientUpdateOptions Update { get; set; } = new();
}

/// <summary>
/// Metadados da versão de atualização do agente entregues por
/// <c>GET /api/v1/client/update</c> (R16.1). Vazio quando não há atualização
/// publicada.
/// </summary>
public sealed class ClientUpdateOptions
{
    /// <summary>Versão disponível (SemVer). Vazio quando não há atualização.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>URL do pacote no armazenamento (MinIO/S3).</summary>
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>Hash SHA-256 do pacote, para verificação de integridade (R16.3).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Assinatura do pacote, para verificação de autenticidade (R16.3).</summary>
    public string Signature { get; set; } = string.Empty;
}
