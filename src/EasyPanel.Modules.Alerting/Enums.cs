namespace EasyPanel.Modules.Alerting;

/// <summary>Severidade de um <see cref="Alert"/>, definida pela <see cref="AlertRule"/> de origem (R1.4).</summary>
public enum AlertSeverity
{
    /// <summary>Informativa: não exige ação imediata.</summary>
    Informativa = 0,

    /// <summary>Atenção: requer acompanhamento.</summary>
    Atencao = 1,

    /// <summary>Crítica: requer ação imediata.</summary>
    Critica = 2,
}

/// <summary>Escopo de aplicação de uma <see cref="AlertRule"/> (R1.1).</summary>
public enum AlertRuleScopeType
{
    /// <summary>Aplica-se a todo o Tenant.</summary>
    Tenant = 0,

    /// <summary>Aplica-se a um Local específico.</summary>
    Location = 1,

    /// <summary>Aplica-se a uma Impressora específica.</summary>
    Printer = 2,

    /// <summary>Aplica-se a um Windows_Client (agente) específico.</summary>
    WindowsClient = 3,
}

/// <summary>Estado do ciclo de vida de um <see cref="Alert"/> (R3.2).</summary>
public enum AlertState
{
    /// <summary>Alerta gerado, ainda não tratado.</summary>
    Open = 0,

    /// <summary>Alerta reconhecido por um usuário, em tratamento.</summary>
    Acknowledged = 1,

    /// <summary>Alerta resolvido (manual ou automaticamente).</summary>
    Resolved = 2,
}

/// <summary>Canal de notificação de um <see cref="Alert"/> (R4/R5).</summary>
public enum NotificationChannel
{
    /// <summary>Notificação por e-mail (R4).</summary>
    Email = 0,

    /// <summary>Notificação por webhook de saída assinado (R5).</summary>
    Webhook = 1,
}

/// <summary>Resultado de uma tentativa de notificação (R4.3/R5.4/R7.3).</summary>
public enum NotificationOutcome
{
    /// <summary>Notificação entregue com sucesso.</summary>
    Success = 0,

    /// <summary>Falha ao entregar a notificação.</summary>
    Failure = 1,

    /// <summary>Notificação não despachada por silenciamento vigente (R7.3).</summary>
    Suppressed = 2,
}

/// <summary>Estado de um item da fila de notificação (<see cref="AlertNotificationOutbox"/>).</summary>
public enum OutboxStatus
{
    /// <summary>Aguardando despacho.</summary>
    Pending = 0,

    /// <summary>Despachado com sucesso.</summary>
    Sent = 1,

    /// <summary>Não despachado por silenciamento vigente no momento do envio (R7.3).</summary>
    Suppressed = 2,

    /// <summary>Tentativas esgotadas sem sucesso (R4.4/R5.5).</summary>
    FailedPermanently = 3,
}
