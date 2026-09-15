using Microsoft.AspNetCore.Builder;

namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Extensões de pipeline para a resolução de tenant.
/// </summary>
public static class TenancyApplicationBuilderExtensions
{
    /// <summary>
    /// Registra o <see cref="TenantResolutionMiddleware"/> no pipeline HTTP.
    ///
    /// Deve ser posicionado <b>após</b> a autenticação (para que o
    /// <c>ClaimsPrincipal</c> já esteja populado) e antes dos componentes que
    /// dependem do tenant resolvido (autorização por tenant, filtros do EF). É
    /// seguro para requisições anônimas: nesse caso o contexto permanece sem
    /// tenant (R6.3).
    /// </summary>
    /// <param name="app">Builder do pipeline da aplicação.</param>
    /// <returns>O próprio <paramref name="app"/> para encadeamento.</returns>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
