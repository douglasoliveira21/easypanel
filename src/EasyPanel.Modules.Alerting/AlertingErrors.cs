using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Alerting;

/// <summary>Erros de domínio do módulo de Alertas (R1/R3/R7), no padrão de <c>MonitoringErrors</c>.</summary>
public static class AlertingErrors
{
    /// <summary>Recurso inexistente ou de outro tenant. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "alerting.not_found",
        "Recurso não encontrado.");

    /// <summary>Escopo de regra/silenciamento inválido para o tenant corrente (R1.3). → 400.</summary>
    public static readonly Error InvalidScope = Error.Validation(
        "alerting.scope.invalid",
        "Escopo informado é inválido ou não pertence ao tenant corrente.");

    /// <summary>Nenhum tipo de evento informado para a regra (R1.1). → 400.</summary>
    public static readonly Error EventTypesRequired = Error.Validation(
        "alerting.rule.event_types_required",
        "A regra deve informar ao menos um tipo de evento observado.");

    /// <summary>Limiar sem janela de tempo, ou janela sem limiar (R1.1). → 400.</summary>
    public static readonly Error InvalidThreshold = Error.Validation(
        "alerting.rule.threshold_invalid",
        "Limiar de ocorrências e janela de tempo devem ser informados juntos.");

    /// <summary>Canal de e-mail habilitado sem destinatários (R4.1). → 400.</summary>
    public static readonly Error EmailRecipientsRequired = Error.Validation(
        "alerting.rule.email_recipients_required",
        "Informe ao menos um destinatário de e-mail para habilitar o canal.");

    /// <summary>Canal de webhook habilitado sem URL válida (R5.1). → 400.</summary>
    public static readonly Error WebhookUrlRequired = Error.Validation(
        "alerting.rule.webhook_url_required",
        "Informe a URL do webhook para habilitar o canal.");

    /// <summary>URL de webhook que não usa HTTPS (R5.3). → 400.</summary>
    public static readonly Error WebhookUrlNotHttps = Error.Validation(
        "alerting.rule.webhook_url_not_https",
        "A URL do webhook deve usar HTTPS.");

    /// <summary>Silenciamento sem nenhum escopo informado (R7.1). → 400.</summary>
    public static readonly Error SilenceScopeRequired = Error.Validation(
        "alerting.silence.scope_required",
        "Informe ao menos uma regra, impressora ou agente para o silenciamento.");

    /// <summary>Período de silenciamento inválido (término não posterior ao início) (R7.1). → 400.</summary>
    public static readonly Error InvalidSilencePeriod = Error.Validation(
        "alerting.silence.period_invalid",
        "O término do silenciamento deve ser posterior ao início.");

    /// <summary>Silenciamento já encerrado. → 409.</summary>
    public static readonly Error SilenceAlreadyEnded = Error.Conflict(
        "alerting.silence.already_ended",
        "O silenciamento já foi encerrado.");

    /// <summary>Transição de estado de Alerta inválida (ex.: resolver um Alerta já resolvido) (R3.3/R3.4). → 409.</summary>
    public static readonly Error InvalidAlertTransition = Error.Conflict(
        "alerting.alert.invalid_transition",
        "Transição de estado inválida para o Alerta no estado atual.");
}
