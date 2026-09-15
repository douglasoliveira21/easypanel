using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Contracts;

/// <summary>
/// Vínculo de escopo de um <see cref="Contract"/> a uma Impressora (Fase 7 —
/// R2). Impressora é referenciada apenas por <c>Guid</c>; a validação de que
/// pertence ao mesmo Cliente do contrato acontece em
/// <c>Infrastructure.Contracts</c>.
/// </summary>
public class ContractPrinter : TenantEntity
{
    /// <summary>Contrato ao qual este vínculo pertence.</summary>
    public required Guid ContractId { get; set; }

    /// <summary>Impressora coberta pelo contrato.</summary>
    public required Guid PrinterId { get; set; }
}
