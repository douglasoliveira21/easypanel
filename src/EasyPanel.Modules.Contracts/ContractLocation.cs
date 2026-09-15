using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Contracts;

/// <summary>
/// Vínculo de escopo de um <see cref="Contract"/> a um Local (Fase 7 — R2).
/// Local é referenciado apenas por <c>Guid</c>; a validação de que pertence
/// ao mesmo Cliente do contrato acontece em <c>Infrastructure.Contracts</c>.
/// </summary>
public class ContractLocation : TenantEntity
{
    /// <summary>Contrato ao qual este vínculo pertence.</summary>
    public required Guid ContractId { get; set; }

    /// <summary>Local coberto pelo contrato.</summary>
    public required Guid LocationId { get; set; }
}
