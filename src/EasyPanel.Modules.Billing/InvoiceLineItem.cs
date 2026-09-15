using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Billing;

/// <summary>
/// Item de excedente de uma <see cref="Invoice"/>, por (Impressora,
/// CounterType) (Fase 8 — R4.3). Só é criado quando <see cref="ExcessQuantity"/>
/// &gt; 0. Somente-adição; imutável depois que a Fatura sai de <c>Rascunho</c>.
/// </summary>
public class InvoiceLineItem : TenantEntity
{
    /// <summary>Fatura à qual este item pertence.</summary>
    public required Guid InvoiceId { get; set; }

    /// <summary>Impressora faturada.</summary>
    public required Guid PrinterId { get; set; }

    /// <summary>Tipo de contador faturado.</summary>
    public required BillingCounterType CounterType { get; set; }

    /// <summary>Rótulo livre, copiado da Franquia quando <see cref="CounterType"/> = <see cref="BillingCounterType.Other"/>.</summary>
    public string? CounterTypeLabel { get; set; }

    /// <summary>Consumo do período (R2), nunca negativo.</summary>
    public required long ConsumedQuantity { get; set; }

    /// <summary>Quantidade incluída, copiada da Franquia no momento do fechamento.</summary>
    public required long IncludedQuantity { get; set; }

    /// <summary>Excedente: <c>max(0, Consumido − Incluído)</c>.</summary>
    public required long ExcessQuantity { get; set; }

    /// <summary>Preço unitário do excedente, copiado da Franquia.</summary>
    public required decimal UnitPrice { get; set; }

    /// <summary>Valor do item: <c>ExcessQuantity × UnitPrice</c>, arredondado bancário a 2 casas.</summary>
    public required decimal LineAmount { get; set; }
}
