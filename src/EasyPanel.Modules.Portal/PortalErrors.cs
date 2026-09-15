using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Portal;

/// <summary>
/// Erros esperados dos serviços de leitura do Portal do Cliente (Fase 10),
/// expostos como valores estáveis para uso pelos serviços e mapeamento na
/// camada de API.
/// </summary>
public static class PortalErrors
{
    /// <summary>
    /// O recurso (Impressora, Chamado, Fatura) não existe, pertence a outro
    /// Cliente do mesmo tenant, pertence a outro tenant, ou o Escopo de Cliente
    /// não está resolvido (R3.2, R4.2, R5.3, R6.3). Sempre 404 — nunca distingue
    /// esses casos entre si (não-enumeração).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "portal.not_found",
        "Recurso não encontrado.");
}
