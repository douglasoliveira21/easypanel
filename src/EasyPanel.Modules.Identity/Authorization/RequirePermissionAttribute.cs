using Microsoft.AspNetCore.Authorization;

namespace EasyPanel.Modules.Identity.Authorization;

/// <summary>
/// Exige que o usuário autenticado possua uma permissão granular específica para
/// executar a operação anotada (R5.3/R5.4). Aplicado a um controller ou action,
/// vincula a uma política de autorização nomeada <c>perm:&lt;permission&gt;</c>
/// (ver <see cref="PermissionPolicy"/>).
///
/// <para>Herda de <see cref="AuthorizeAttribute"/>, de modo que a exigência de
/// autenticação (401 quando ausente) e a avaliação de política (403 quando a
/// permissão falta) são feitas pelo pipeline padrão de autorização do ASP.NET
/// Core — sempre no backend, independentemente do frontend (R5.6). Um
/// <c>IAuthorizationPolicyProvider</c> dinâmico materializa a política sob demanda
/// e um handler resolve as permissões efetivas dos papéis do usuário.</para>
///
/// <para>Exemplo: <c>[RequirePermission(Permissions.CustomerView)]</c>.</para>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    /// <summary>
    /// Cria o atributo exigindo a <paramref name="permission"/> informada (ex.:
    /// <c>customer.view</c>), definindo <see cref="AuthorizeAttribute.Policy"/>
    /// como <c>perm:&lt;permission&gt;</c>.
    /// </summary>
    /// <param name="permission">Permissão exigida, do catálogo <see cref="Permissions"/>.</param>
    public RequirePermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
        Policy = PermissionPolicy.ForPermission(permission);
    }

    /// <summary>A permissão exigida por este atributo.</summary>
    public string Permission { get; }
}
