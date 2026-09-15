using System.ComponentModel.DataAnnotations;

namespace EasyPanel.Infrastructure.Alerting;

/// <summary>
/// Parâmetros operacionais do motor de alertas e do despacho de notificação
/// (Fase 3), vinculados à seção <c>Alerting</c> da configuração.
/// </summary>
public sealed class AlertingOptions
{
    /// <summary>Nome da seção de configuração.</summary>
    public const string SectionName = "Alerting";

    /// <summary>Intervalo (em segundos) entre varreduras do <c>AlertEngine</c>.</summary>
    [Range(5, 3_600)]
    public int EngineScanIntervalSeconds { get; set; } = 30;

    /// <summary>Máximo de <c>PrinterEvent</c> processados por varredura (R2.1).</summary>
    [Range(1, 10_000)]
    public int EventBatchSize { get; set; } = 500;

    /// <summary>Intervalo (em segundos) entre execuções do <c>AlertNotificationDispatcher</c>.</summary>
    [Range(5, 3_600)]
    public int DispatchIntervalSeconds { get; set; } = 15;

    /// <summary>Máximo de tentativas de envio de uma notificação antes de <c>FailedPermanently</c> (R4.4/R5.5).</summary>
    [Range(1, 20)]
    public int MaxNotificationAttempts { get; set; } = 5;

    /// <summary>Parâmetros de conexão SMTP para o canal de e-mail (R4).</summary>
    public AlertSmtpOptions Smtp { get; set; } = new();
}

/// <summary>
/// Configuração de um servidor SMTP para envio de notificações de alerta por
/// e-mail (R4). Quando <see cref="Host"/> está vazio, o sistema usa um envio
/// log-only (mesmo padrão de <c>LogOnlyPasswordResetNotifier</c> da Fase 1).
/// </summary>
public sealed class AlertSmtpOptions
{
    /// <summary>Host do servidor SMTP. Vazio desativa o envio real (log-only).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Porta do servidor SMTP.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Indica se a conexão usa TLS/SSL.</summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>Usuário de autenticação SMTP, quando exigido pelo servidor.</summary>
    public string? User { get; set; }

    /// <summary>Senha de autenticação SMTP, quando exigido pelo servidor.</summary>
    public string? Password { get; set; }

    /// <summary>Endereço de remetente das notificações.</summary>
    public string FromAddress { get; set; } = "alerts@easypanel.local";
}
