using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Middleware que resolve o tenant da requisição corrente a partir do contexto
/// de autenticação, executando <b>após</b> a autenticação no pipeline.
///
/// Regras (R6.3, R6.7):
/// <list type="bullet">
///   <item>
///     Para requisições não autenticadas, o contexto permanece sem tenant
///     (nenhuma resolução é feita).
///   </item>
///   <item>
///     Para usuários comuns autenticados, o tenant é resolvido exclusivamente a
///     partir do claim <see cref="TenancyConstants.TenantIdClaimType"/>. Valores
///     de corpo/query <b>nunca</b> são consultados.
///   </item>
///   <item>
///     Para Super Admins, o contexto é marcado como Super Admin; o tenant alvo é
///     resolvido apenas a partir do cabeçalho administrativo designado
///     (<see cref="TenancyConstants.AdminTenantHeader"/>). Na ausência do
///     cabeçalho, o Super Admin opera fora de um tenant específico.
///   </item>
/// </list>
///
/// O middleware é registrado como singleton pelo pipeline; portanto o
/// <see cref="TenantContext"/> scoped é resolvido a partir de
/// <see cref="HttpContext.RequestServices"/> dentro de <see cref="InvokeAsync"/>.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Inicializa o middleware com o próximo delegate do pipeline.
    /// </summary>
    /// <param name="next">Próximo componente do pipeline HTTP.</param>
    public TenantResolutionMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>
    /// Resolve o tenant para a requisição corrente e prossegue no pipeline.
    /// </summary>
    /// <param name="context">Contexto HTTP da requisição.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Resolve(context);

        await _next(context);
    }

    private static void Resolve(HttpContext context)
    {
        var principal = context.User;

        // Requisição não autenticada: contexto permanece sem tenant.
        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();

        // Super Admin: o tenant alvo vem apenas do cabeçalho administrativo
        // designado; nunca do claim de tenant nem de entrada de negócio (R6.7).
        if (principal.IsInRole(TenancyConstants.SuperAdminRole))
        {
            if (TryReadAdminTenantHeader(context, out var adminTenantId))
            {
                tenantContext.SetSuperAdmin(adminTenantId);
            }
            else
            {
                tenantContext.SetSuperAdmin();
            }

            return;
        }

        // Usuário comum: tenant resolvido exclusivamente do claim (R6.3).
        var tenantClaim = principal.FindFirstValue(TenancyConstants.TenantIdClaimType);
        if (Guid.TryParse(tenantClaim, out var tenantId))
        {
            tenantContext.SetTenant(tenantId);
        }

        // Fase 10 (Portal do Cliente): segundo nível de isolamento, resolvido do
        // claim customer_id, presente apenas para usuários do papel Cliente (R2.2).
        var customerClaim = principal.FindFirstValue(TenancyConstants.CustomerIdClaimType);
        if (Guid.TryParse(customerClaim, out var customerId))
        {
            var customerContext = context.RequestServices.GetRequiredService<CustomerContext>();
            customerContext.SetCustomer(customerId);
        }
    }

    private static bool TryReadAdminTenantHeader(HttpContext context, out Guid tenantId)
    {
        tenantId = Guid.Empty;

        if (!context.Request.Headers.TryGetValue(TenancyConstants.AdminTenantHeader, out var values))
        {
            return false;
        }

        return Guid.TryParse(values.ToString(), out tenantId);
    }
}
