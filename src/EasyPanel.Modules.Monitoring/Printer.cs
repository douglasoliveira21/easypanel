using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Monitoring;

/// <summary>
/// Impressora monitorada, pertencente à hierarquia Tenant → Cliente → Local (R9).
/// É uma <see cref="TenantEntity"/> — recebe o filtro global por tenant e a
/// validação de escrita do interceptor de <c>SaveChanges</c>, garantindo o
/// isolamento (acesso cross-tenant → 404).
/// </summary>
public class Printer : TenantEntity
{
    /// <summary>Cliente (empresa) proprietário do parque (R9.1).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Local vigente onde a impressora está instalada (R9.1/R10.4).</summary>
    public Guid LocationId { get; set; }

    /// <summary>Fabricante (normalizado pelo driver — R8).</summary>
    public string? Fabricante { get; set; }

    /// <summary>Modelo do equipamento.</summary>
    public string? Modelo { get; set; }

    /// <summary>Número de série.</summary>
    public string? NumeroSerie { get; set; }

    /// <summary>Patrimônio (asset tag) atribuído pela organização.</summary>
    public string? Patrimonio { get; set; }

    /// <summary>Endereço IP atual.</summary>
    public string? Ip { get; set; }

    /// <summary>Endereço MAC.</summary>
    public string? Mac { get; set; }

    /// <summary>Hostname da impressora na rede.</summary>
    public string? Hostname { get; set; }

    /// <summary>Protocolo de monitoramento (ex.: SNMP).</summary>
    public string? Protocolo { get; set; }

    /// <summary>Porta de monitoramento (ex.: 161 para SNMP).</summary>
    public int? Porta { get; set; }

    /// <summary>Status atual (R9.2).</summary>
    public PrinterStatus Status { get; set; } = PrinterStatus.Unknown;

    /// <summary>Indica se o monitoramento está ativo (R9.1/R7.6).</summary>
    public bool MonitoringEnabled { get; set; }

    /// <summary>Data de instalação (R9.1).</summary>
    public DateTimeOffset? InstalledAt { get; set; }

    /// <summary>Observações livres.</summary>
    public string? Observacoes { get; set; }
}
