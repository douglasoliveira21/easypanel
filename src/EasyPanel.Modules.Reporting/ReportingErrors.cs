using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Reporting;

/// <summary>Erros de domínio do módulo de Relatórios (Fase 9), no padrão de <c>BillingErrors</c>/<c>ContractErrors</c>.</summary>
public static class ReportingErrors
{
    /// <summary>Data de fim anterior à data de início (R2.3). → 400.</summary>
    public static readonly Error InvalidDateRange = Error.Validation(
        "reporting.invalid_date_range",
        "A data de fim não pode ser anterior à data de início.");

    /// <summary>Intervalo de datas maior que o limite permitido (R6.5). → 400.</summary>
    public static readonly Error DateRangeTooLarge = Error.Validation(
        "reporting.date_range_too_large",
        "O intervalo de datas não pode exceder 366 dias.");
}
