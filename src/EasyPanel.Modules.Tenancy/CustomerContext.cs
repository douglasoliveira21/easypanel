namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Implementação <c>scoped</c> e mutável de <see cref="ICustomerContext"/>
/// (Fase 10 — Portal do Cliente).
///
/// O Cliente resolvido é preenchido pela camada de resolução
/// (<see cref="TenantResolutionMiddleware"/>), por meio de <see cref="SetCustomer"/>.
/// Enquanto não resolvido, o contexto permanece sem Cliente
/// (<see cref="HasCustomer"/> retorna <c>false</c>) — o caso de todo usuário que
/// não é do papel <c>Cliente</c>.
/// </summary>
public sealed class CustomerContext : ICustomerContext
{
    /// <inheritdoc />
    public Guid? CustomerId { get; private set; }

    /// <inheritdoc />
    public bool HasCustomer => CustomerId.HasValue;

    /// <summary>
    /// Define o Cliente resolvido para a requisição corrente, a partir da claim
    /// <c>customer_id</c> do token autenticado.
    /// </summary>
    /// <param name="customerId">Identificador do Cliente vinculado ao usuário autenticado.</param>
    public void SetCustomer(Guid customerId)
    {
        CustomerId = customerId;
    }
}
