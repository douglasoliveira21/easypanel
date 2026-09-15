using Microsoft.AspNetCore.Authorization;

namespace EasyPanel.Modules.Identity.Authorization;

/// <summary>
/// Requisito de autorização (R5.3) que exige que o usuário autenticado possua uma
/// permissão granular específica. Cada política <c>perm:&lt;permission&gt;</c>
/// carrega exatamente um <see cref="PermissionRequirement"/> com a permissão
/// exigida; um <see cref="AuthorizationHandler{TRequirement}"/> na Infrastructure
/// resolve as permissões efetivas dos papéis do usuário e satisfaz (ou não) o
/// requisito.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>Cria o requisito para a permissão informada.</summary>
    /// <param name="permission">Permissão exigida (ex.: <c>customer.view</c>).</param>
    public PermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <summary>Permissão exigida para satisfazer o requisito.</summary>
    public string Permission { get; }
}
