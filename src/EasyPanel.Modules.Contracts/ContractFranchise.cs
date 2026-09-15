using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Contracts;

/// <summary>
/// Franquia de um <see cref="Contract"/> por tipo de contador (Fase 7 — R3):
/// quantidade incluída por ciclo e preço único de excedente. No máximo uma
/// linha por (Contrato, CounterType) — upsert.
/// </summary>
public class ContractFranchise : TenantEntity
{
    /// <summary>Contrato ao qual esta franquia pertence.</summary>
    public required Guid ContractId { get; set; }

    /// <summary>Tipo de contador coberto.</summary>
    public required ContractCounterType CounterType { get; set; }

    /// <summary>Rótulo livre, usado quando <see cref="CounterType"/> = <see cref="ContractCounterType.Other"/>.</summary>
    public string? CounterTypeLabel { get; set; }

    /// <summary>Quantidade incluída por ciclo de faturamento (≥ 0).</summary>
    public required long IncludedQuantity { get; set; }

    /// <summary>Preço unitário do excedente (≥ 0), preço único (sem faixas de volume).</summary>
    public required decimal ExcessUnitPrice { get; set; }

    /// <summary>Moeda (ISO 4217), fixa <c>"BRL"</c> nesta fase.</summary>
    public string Currency { get; set; } = "BRL";
}
