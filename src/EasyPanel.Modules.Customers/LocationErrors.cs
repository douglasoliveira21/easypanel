using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Customers;

/// <summary>
/// Erros esperados do cadastro de locais (R9), expostos como valores estáveis
/// para uso pelo <see cref="ILocationService"/> e mapeamento na camada de API.
/// Cada erro carrega uma <see cref="ErrorType"/> traduzida em HTTP (400/404).
/// </summary>
public static class LocationErrors
{
    /// <summary>
    /// Não há tenant resolvido no contexto para vincular o local (R9.1). Mapeado
    /// para HTTP 400 (validação).
    /// </summary>
    public static readonly Error NoTenantContext = Error.Validation(
        "location.tenant.missing",
        "Não há tenant no contexto para criar o local.");

    /// <summary>Nome do local ausente/vazio (R9.2). HTTP 400.</summary>
    public static readonly Error MissingNome = Error.Validation(
        "location.nome.missing",
        "O nome do local é obrigatório.");

    /// <summary>Status fora do conjunto fechado {Ativo, Inativo} (R9.8). HTTP 400.</summary>
    public static readonly Error InvalidStatus = Error.Validation(
        "location.status.invalid",
        "O status informado é inválido.");

    /// <summary>
    /// O cliente referenciado não existe ou pertence a outro tenant (R9.4).
    /// Mapeado para HTTP 400 (validação): o vínculo informado é inválido.
    /// </summary>
    public static readonly Error CustomerNotFound = Error.Validation(
        "location.customer.invalid",
        "O cliente informado é inexistente ou pertence a outro tenant.");

    /// <summary>
    /// Local inexistente ou pertencente a outro tenant (R9.6). Retornado como
    /// <see cref="ErrorType.NotFound"/> (HTTP 404) para não revelar a existência
    /// de locais de outros tenants (não-vazamento).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "location.not_found",
        "Local não encontrado.");
}
