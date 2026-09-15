using EasyPanel.Modules.Monitoring;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação <c>scoped</c> e mutável de <see cref="IClientContext"/> (R4.2).
///
/// O agente autenticado é preenchido pelo handler de autenticação do cliente
/// (<see cref="ClientAuthenticationHandler"/>) a partir dos claims do token,
/// nunca de valores da requisição. Enquanto não resolvido, o contexto permanece
/// sem agente (<see cref="IsAuthenticated"/> retorna <c>false</c>).
/// </summary>
public sealed class ClientContext : IClientContext
{
    /// <inheritdoc />
    public Guid? ClientId { get; private set; }

    /// <inheritdoc />
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    public Guid? LocationId { get; private set; }

    /// <inheritdoc />
    public bool IsAuthenticated => ClientId.HasValue;

    /// <summary>
    /// Define o agente autenticado resolvido da identidade do token do cliente
    /// (R4.2). Tenant e local são sempre derivados do token, jamais da requisição.
    /// </summary>
    public void SetClient(Guid clientId, Guid tenantId, Guid locationId)
    {
        ClientId = clientId;
        TenantId = tenantId;
        LocationId = locationId;
    }
}
