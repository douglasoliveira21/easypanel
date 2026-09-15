namespace EasyPanel.WindowsClient;

/// <summary>Impressora monitorável reportada pelo backend (Fase 4 — R1.2).</summary>
/// <param name="PrinterId">Identificador da impressora no backend.</param>
/// <param name="Ip">Endereço IP/host para consulta SNMP.</param>
/// <param name="Protocolo">Protocolo de monitoramento, quando informado.</param>
/// <param name="Porta">Porta de monitoramento, quando informada.</param>
/// <param name="Fabricante">Fabricante, quando conhecido pelo backend.</param>
public sealed record AgentMonitoredPrinter(Guid PrinterId, string Ip, string? Protocolo, int? Porta, string? Fabricante);

/// <summary>Configuração operacional do agente (Fase 2/R15.2; suprimento — Fase 4).</summary>
/// <param name="CollectionIntervalSeconds">Intervalo entre ciclos de coleta.</param>
/// <param name="DiscoveryTargets">Alvos de descoberta configurados.</param>
/// <param name="IgnoredPrinters">Impressoras ignoradas na descoberta.</param>
/// <param name="MonitoredPrinters">Impressoras já registradas e monitoráveis (Fase 4 — R1.2).</param>
public sealed record AgentConfig(
    int CollectionIntervalSeconds,
    IReadOnlyList<string> DiscoveryTargets,
    IReadOnlyList<string> IgnoredPrinters,
    IReadOnlyList<AgentMonitoredPrinter> MonitoredPrinters);

/// <summary>
/// Cliente de saída do agente para a API do backend (R5.1). Toda comunicação é
/// HTTPS com validação de certificado TLS; falhas de validação abortam a operação
/// e são logadas (R5.2), nunca ignorando o erro de certificado. Nenhum segredo é
/// registrado (R5.6).
/// </summary>
public interface IBackendClient
{
    /// <summary>
    /// Envia uma submissão de coleta ao endpoint idempotente <c>/client/collect</c>.
    /// Retorna <c>true</c> em sucesso (2xx); <c>false</c> em falha transitória de
    /// rede/servidor (para reenfileirar/retentar).
    /// </summary>
    Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct);

    /// <summary>
    /// Obtém a configuração vigente do agente (<c>GET /client/config</c>) — Fase 4,
    /// R1.2. Retorna <c>null</c> em falha (o chamador mantém a última configuração
    /// válida conhecida, R15.4 já vigente).
    /// </summary>
    Task<AgentConfig?> GetConfigAsync(CancellationToken ct);
}
