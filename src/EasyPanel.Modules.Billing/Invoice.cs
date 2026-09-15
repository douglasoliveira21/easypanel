using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Billing;

/// <summary>
/// Fatura gerada por um fechamento para um Contrato e um período (Fase 8 —
/// R4/R5). Contrato é referenciado apenas por <c>Guid</c>;
/// <see cref="CustomerId"/> é copiado do Contrato no momento da geração,
/// estável mesmo que o Contrato mude depois.
/// </summary>
public class Invoice : TenantEntity
{
    /// <summary>Contrato ao qual esta fatura se refere.</summary>
    public required Guid ContractId { get; set; }

    /// <summary>Cliente do Contrato no momento da geração.</summary>
    public required Guid CustomerId { get; set; }

    /// <summary>Primeiro dia do período faturado, 00:00 UTC.</summary>
    public required DateTimeOffset PeriodStart { get; set; }

    /// <summary>Cursor portável de <see cref="PeriodStart"/>.</summary>
    public long PeriodStartTicks { get; set; }

    /// <summary>Último dia do período faturado, 23:59:59 UTC.</summary>
    public required DateTimeOffset PeriodEnd { get; set; }

    /// <summary>Cursor portável de <see cref="PeriodEnd"/>.</summary>
    public long PeriodEndTicks { get; set; }

    /// <summary>Status corrente do ciclo de vida.</summary>
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Rascunho;

    /// <summary>Soma dos valores dos Itens de Fatura.</summary>
    public required decimal TotalAmount { get; set; }

    /// <summary>Moeda (ISO 4217), fixa <c>"BRL"</c> nesta fase.</summary>
    public string Currency { get; set; } = "BRL";

    /// <summary>Instante de geração pelo fechamento.</summary>
    public required DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Cursor portável de <see cref="GeneratedAt"/>.</summary>
    public long GeneratedAtTicks { get; set; }

    /// <summary>Instante da transição para <see cref="InvoiceStatus.Emitida"/>.</summary>
    public DateTimeOffset? IssuedAt { get; set; }

    /// <summary>Instante da transição para <see cref="InvoiceStatus.Cancelada"/>.</summary>
    public DateTimeOffset? CancelledAt { get; set; }
}
