using EasyPanel.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Security.Authorization;

/// <summary>
/// <see cref="IAuthorizationPolicyProvider"/> que materializa políticas de
/// permissão <c>perm:&lt;permission&gt;</c> sob demanda (R5), evitando o registro
/// estático de uma política por permissão do catálogo.
///
/// <para>Para um nome de política com o prefixo <c>perm:</c> (ver
/// <see cref="PermissionPolicy"/>), constrói uma <see cref="AuthorizationPolicy"/>
/// que exige autenticação e um único <see cref="PermissionRequirement"/> com a
/// permissão extraída do nome. Para quaisquer outros nomes — bem como para a
/// política padrão e a de fallback — delega ao
/// <see cref="DefaultAuthorizationPolicyProvider"/>, preservando o comportamento
/// padrão do ASP.NET Core (ex.: <c>[Authorize]</c> simples).</para>
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    /// <summary>
    /// Cria o provider encadeando um <see cref="DefaultAuthorizationPolicyProvider"/>
    /// para políticas não-<c>perm:</c> e para as políticas padrão/fallback.
    /// </summary>
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        var permission = PermissionPolicy.ExtractPermission(policyName);
        if (permission is not null)
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        // Não é uma política de permissão: delega ao provider padrão (políticas
        // nomeadas registradas estaticamente, se houver).
        return _fallback.GetPolicyAsync(policyName);
    }
}
