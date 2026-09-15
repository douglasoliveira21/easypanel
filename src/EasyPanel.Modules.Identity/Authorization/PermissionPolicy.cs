namespace EasyPanel.Modules.Identity.Authorization;

/// <summary>
/// Convenções de nomenclatura das políticas de autorização por permissão (R5).
///
/// Uma política de permissão tem o nome <c>perm:&lt;permission&gt;</c> (ex.:
/// <c>perm:customer.view</c>). O prefixo permite que um
/// <c>IAuthorizationPolicyProvider</c> dinâmico reconheça e materialize essas
/// políticas sob demanda, sem exigir o registro estático de uma política por
/// permissão. Centralizar o prefixo aqui garante que o atributo
/// (<see cref="RequirePermissionAttribute"/>) e o provider (na Infrastructure)
/// concordem sobre o formato.
/// </summary>
public static class PermissionPolicy
{
    /// <summary>Prefixo das políticas de permissão (<c>perm:</c>).</summary>
    public const string Prefix = "perm:";

    /// <summary>
    /// Compõe o nome da política para a permissão informada
    /// (<c>perm:&lt;permission&gt;</c>).
    /// </summary>
    public static string ForPermission(string permission) => Prefix + permission;

    /// <summary>Indica se o nome de política informado é uma política de permissão.</summary>
    public static bool IsPermissionPolicy(string policyName) =>
        policyName is not null && policyName.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Extrai a permissão de um nome de política <c>perm:&lt;permission&gt;</c>.
    /// Retorna <c>null</c> se o nome não for uma política de permissão ou se a
    /// permissão estiver vazia.
    /// </summary>
    public static string? ExtractPermission(string policyName)
    {
        if (!IsPermissionPolicy(policyName))
        {
            return null;
        }

        var permission = policyName[Prefix.Length..];
        return string.IsNullOrWhiteSpace(permission) ? null : permission;
    }
}
