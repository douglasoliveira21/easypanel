namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Contexto de Cliente (Customer) da requisição corrente (Fase 10 — Portal do
/// Cliente, R2.2). Segundo nível de isolamento, análogo a
/// <see cref="ITenantContext"/>, mas restrito a usuários do papel <c>Cliente</c>:
/// dentro do tenant já resolvido, escopa ainda mais as consultas do portal ao
/// Cliente vinculado ao usuário autenticado.
///
/// O <see cref="CustomerId"/> é sempre derivado do token autenticado (claim
/// <c>customer_id</c>), nunca de entrada fornecida pelo cliente HTTP. É
/// registrado com tempo de vida <c>scoped</c>, de modo que cada requisição possua
/// seu próprio contexto.
/// </summary>
public interface ICustomerContext
{
    /// <summary>
    /// Identificador do Cliente resolvido para a requisição, ou <c>null</c>
    /// quando não autenticado como usuário do portal ou quando a claim de
    /// Cliente não está presente/válida.
    /// </summary>
    Guid? CustomerId { get; }

    /// <summary>Indica se há um Cliente resolvido no contexto corrente.</summary>
    bool HasCustomer { get; }
}
