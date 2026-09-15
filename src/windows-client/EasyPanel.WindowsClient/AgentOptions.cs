namespace EasyPanel.WindowsClient;

/// <summary>
/// Configuração local do agente (R1.4): endpoint do backend, credenciais próprias
/// (client_id/secret obtidos no registro), caminho da fila local e intervalos.
/// Segredos vêm de configuração protegida da máquina, nunca versionados.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Agent";

    /// <summary>URL base HTTPS da API do backend (R5.1).</summary>
    public string BackendBaseUrl { get; set; } = string.Empty;

    /// <summary>Identificador do agente emitido no registro (client_id).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Segredo próprio do agente (armazenado localmente protegido).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Chave de provisionamento do Local, usada no primeiro registro (R3.2).</summary>
    public string ProvisioningKey { get; set; } = string.Empty;

    /// <summary>Identificador único e persistente deste agente (R3.1).</summary>
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>Caminho do arquivo SQLite da fila local offline (R5.4).</summary>
    public string LocalQueuePath { get; set; } = "queue.db";

    /// <summary>Intervalo (segundos) entre varreduras do Guardian (R2.2).</summary>
    public int GuardianIntervalSeconds { get; set; } = 30;

    /// <summary>Intervalo (segundos) entre tentativas de reenvio da fila (R5.4).</summary>
    public int ResendIntervalSeconds { get; set; } = 60;

    /// <summary>Intervalo (segundos) entre heartbeats (R6.2).</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 60;
}
