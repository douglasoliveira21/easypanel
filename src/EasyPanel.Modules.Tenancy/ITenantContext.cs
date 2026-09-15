namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Contexto de tenant da requisição corrente (R6.3).
///
/// O <see cref="TenantId"/> é sempre derivado do token autenticado (ou do
/// endpoint administrativo designado para Super Admin), nunca de entrada
/// fornecida pelo cliente. É registrado com tempo de vida <c>scoped</c>, de
/// modo que cada requisição possua seu próprio contexto.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Identificador do tenant resolvido para a requisição, ou <c>null</c>
    /// quando não autenticado ou quando um Super Admin opera fora de um tenant
    /// específico.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>Indica se o contexto corrente é de um Super Admin da plataforma.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>Indica se há um tenant resolvido no contexto corrente.</summary>
    bool HasTenant { get; }
}
