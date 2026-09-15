using EasyPanel.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Security.Authorization;

/// <summary>
/// Registro de DI da autorização por permissão (RBAC — R5): o resolvedor de
/// permissões, o handler de autorização e o policy provider dinâmico que
/// materializa políticas <c>perm:&lt;permission&gt;</c> sob demanda.
///
/// <para>Também chama <c>AddAuthorization()</c> para garantir que os serviços
/// centrais de autorização do ASP.NET Core estejam registrados (idempotente caso
/// já tenham sido adicionados por <c>UseAuthorization</c>/outro registro).</para>
///
/// <para>Deve ser composto após a autenticação Bearer
/// (<c>AddJwtAuthentication</c>), pois a avaliação de permissões depende do
/// <c>ClaimsPrincipal</c> autenticado; a ordem de registro em DI, contudo, não é
/// significativa — apenas a ordem dos middlewares no pipeline.</para>
/// </summary>
public static class PermissionAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="IPermissionResolver"/> (em memória, a partir do
    /// catálogo em código), o <see cref="PermissionAuthorizationHandler"/> e o
    /// <see cref="PermissionPolicyProvider"/> dinâmico.
    /// </summary>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        // Garante os serviços centrais de autorização.
        services.AddAuthorization();

        // Resolvedor de permissões efetivas por papel (em memória — ver decisão de
        // projeto em RolePermissionResolver). Singleton: sem estado e sem I/O.
        services.TryAddSingleton<IPermissionResolver, RolePermissionResolver>();

        // Handler do PermissionRequirement. Singleton pois só depende do resolver
        // (também singleton) e não mantém estado por requisição.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, PermissionAuthorizationHandler>());

        // Policy provider dinâmico para políticas perm:<permission>. Substitui o
        // provider padrão registrado por AddAuthorization (daí Replace, não TryAdd),
        // encadeando internamente o DefaultAuthorizationPolicyProvider como fallback.
        services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());

        return services;
    }
}
