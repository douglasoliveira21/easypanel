using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Billing;

/// <summary>
/// Registro somente-adição da execução de um fechamento mensal (Fase 8 —
/// R1). No máximo uma linha por (Tenant, Ano, Mês) — impede refechamento
/// silencioso (R1.3).
/// </summary>
public class BillingClosing : TenantEntity
{
    /// <summary>Ano do período fechado.</summary>
    public required int Year { get; set; }

    /// <summary>Mês do período fechado (1–12).</summary>
    public required int Month { get; set; }

    /// <summary>Primeiro dia do período, 00:00 UTC.</summary>
    public required DateTimeOffset PeriodStart { get; set; }

    /// <summary>Cursor portável de <see cref="PeriodStart"/>.</summary>
    public long PeriodStartTicks { get; set; }

    /// <summary>Último dia do período, 23:59:59 UTC.</summary>
    public required DateTimeOffset PeriodEnd { get; set; }

    /// <summary>Cursor portável de <see cref="PeriodEnd"/>.</summary>
    public long PeriodEndTicks { get; set; }

    /// <summary>Instante em que o fechamento foi executado.</summary>
    public required DateTimeOffset ExecutedAt { get; set; }

    /// <summary>Cursor portável de <see cref="ExecutedAt"/>.</summary>
    public long ExecutedAtTicks { get; set; }

    /// <summary>Usuário que executou o fechamento.</summary>
    public Guid? ExecutedByUserId { get; set; }

    /// <summary>Quantidade de Faturas geradas nesta execução.</summary>
    public required int InvoiceCount { get; set; }
}
