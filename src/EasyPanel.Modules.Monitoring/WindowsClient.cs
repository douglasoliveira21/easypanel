using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Agente Windows (Windows Service) instalado em um Local do Cliente, responsável
/// por descobrir e monitorar impressoras e reportar dados ao backend (R3). É uma
/// <see cref="TenantEntity"/> — recebe o filtro global por tenant e a validação de
/// escrita do interceptor de <c>SaveChanges</c>.
///
/// <para>A autenticação do agente é própria (distinta dos JWT de usuário — R4): o
/// segredo do cliente é armazenado apenas como hash em <see cref="SecretHash"/>; o
/// valor bruto nunca é persistido.</para>
/// </summary>
public class WindowsClient : TenantEntity
{
    /// <summary>Cliente (empresa) ao qual o agente pertence, via Local (R3.3).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Local (ponto de instalação) que hospeda o agente (R3.2).</summary>
    public Guid LocationId { get; set; }

    /// <summary>Identificador único e persistente do agente (R3.1).</summary>
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>Hostname do host onde o agente executa (R3.7).</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Versão do agente reportada (R3.7/R16.5).</summary>
    public string AgentVersion { get; set; } = string.Empty;

    /// <summary>Estado atual do agente (R3.7/R6).</summary>
    public WindowsClientState State { get; set; } = WindowsClientState.Registered;

    /// <summary>Horário do último heartbeat recebido (R6.3), quando houver.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    /// <summary>Horário da última coleta concluída (R6.2/R12.5), quando houver.</summary>
    public DateTimeOffset? LastCollectionAt { get; set; }

    /// <summary>Hash do segredo da credencial própria do agente (R4.5). Nunca em texto claro.</summary>
    public string SecretHash { get; set; } = string.Empty;
}
