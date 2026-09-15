using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Alerting;

/// <summary>
/// Regra de alerta configurada por um Tenant (R1): mapeia condições sobre
/// <c>PrinterEvent</c> (Fase 2, referenciado apenas pelo seu tipo — sem navegação
/// entre módulos) a uma severidade e a canais de notificação. Avaliada pelo
/// Motor_de_Alertas (<c>AlertEngine</c>, em Infrastructure).
/// </summary>
public class AlertRule : TenantEntity
{
    /// <summary>Nome da regra.</summary>
    public required string Name { get; set; }

    /// <summary>Descrição livre da regra.</summary>
    public string? Description { get; set; }

    /// <summary>Indica se a regra está ativa (R1.5): inativa não gera novos Alertas.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Tipos de <c>PrinterEventType</c> observados, serializados como CSV de valores
    /// inteiros (ex.: <c>"2,3"</c>). Evita referenciar o enum de Modules.Monitoring
    /// diretamente, preservando o isolamento entre módulos.
    /// </summary>
    public required string EventTypesCsv { get; set; }

    /// <summary>Escopo de aplicação da regra (R1.1).</summary>
    public AlertRuleScopeType ScopeType { get; set; }

    /// <summary>Local alvo, obrigatório quando <see cref="ScopeType"/> = Location.</summary>
    public Guid? ScopeLocationId { get; set; }

    /// <summary>Impressora alvo, obrigatória quando <see cref="ScopeType"/> = Printer.</summary>
    public Guid? ScopePrinterId { get; set; }

    /// <summary>Agente alvo, obrigatório quando <see cref="ScopeType"/> = WindowsClient.</summary>
    public Guid? ScopeWindowsClientId { get; set; }

    /// <summary>Severidade resultante para os Alertas gerados por esta regra (R1.4).</summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>
    /// Número mínimo de ocorrências correspondentes dentro de
    /// <see cref="ThresholdWindowMinutes"/> para disparar o Alerta. Nulo dispara na
    /// primeira ocorrência (R2.3/R2.4).
    /// </summary>
    public int? ThresholdCount { get; set; }

    /// <summary>Janela (minutos) usada com <see cref="ThresholdCount"/>.</summary>
    public int? ThresholdWindowMinutes { get; set; }

    /// <summary>
    /// Quando verdadeiro, o Motor_de_Alertas resolve automaticamente os Alertas
    /// desta regra ao detectar que o alvo voltou a um estado saudável (R2.6).
    /// </summary>
    public bool AutoResolve { get; set; } = true;

    /// <summary>Indica se o canal de e-mail está habilitado para esta regra (R4.1).</summary>
    public bool EmailEnabled { get; set; }

    /// <summary>Destinatários de e-mail, separados por vírgula, quando <see cref="EmailEnabled"/>.</summary>
    public string? EmailRecipientsCsv { get; set; }

    /// <summary>Indica se o canal de webhook está habilitado para esta regra (R5.1).</summary>
    public bool WebhookEnabled { get; set; }

    /// <summary>URL HTTPS do webhook de saída, quando <see cref="WebhookEnabled"/> (R5.3).</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Segredo compartilhado usado para assinar o corpo do webhook (R5.2). Nunca
    /// exposto em DTOs de leitura, logs ou auditoria (R5.6).
    /// </summary>
    public string? WebhookSecret { get; set; }
}
