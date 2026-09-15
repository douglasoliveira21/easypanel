using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Billing;

/// <summary>Erros de domínio do módulo de Fechamento/Faturamento (Fase 8), no padrão de <c>ContractErrors</c>/<c>TicketingErrors</c>.</summary>
public static class BillingErrors
{
    /// <summary>Recurso inexistente ou de outro tenant. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "billing.not_found",
        "Recurso não encontrado.");

    /// <summary>Mês fora do intervalo 1–12 (R1.1). → 400.</summary>
    public static readonly Error InvalidMonth = Error.Validation(
        "billing.closing.invalid_month",
        "O mês deve estar entre 1 e 12.");

    /// <summary>Período corrente ou futuro (R1.2). → 400.</summary>
    public static readonly Error PeriodNotClosed = Error.Validation(
        "billing.closing.period_not_closed",
        "O período informado ainda não terminou.");

    /// <summary>Período já fechado anteriormente (R1.3). → 409.</summary>
    public static readonly Error AlreadyClosed = Error.Conflict(
        "billing.closing.already_closed",
        "Este período já foi fechado anteriormente.");

    /// <summary>Transição de status não permitida a partir do status corrente (R5.4). → 400.</summary>
    public static readonly Error InvalidStatusTransition = Error.Validation(
        "billing.invoice.invalid_status_transition",
        "Transição de status não permitida a partir do status corrente.");
}
