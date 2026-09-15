using EasyPanel.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace EasyPanel.Infrastructure.Security.Authorization;

/// <summary>
/// Handler de autorização que satisfaz um <see cref="PermissionRequirement"/>
/// quando algum papel do usuário autenticado concede a permissão exigida (R5.3).
///
/// <para>Delega a resolução das permissões efetivas ao
/// <see cref="IPermissionResolver"/>, que lê os papéis do
/// <see cref="AuthorizationHandlerContext.User"/> (via
/// <c>ClaimsPrincipal.IsInRole</c>) e os une às permissões do catálogo em código.
/// Se a permissão exigida estiver presente, o requisito é satisfeito
/// (<see cref="AuthorizationHandlerContext.Succeed"/>); caso contrário, o handler
/// simplesmente não o satisfaz — o pipeline de autorização então resulta em HTTP
/// 403 (R5.4). A avaliação ocorre sempre no backend, independentemente do
/// frontend (R5.6).</para>
///
/// <para><b>Super Admin.</b> Nenhum tratamento especial é necessário: o papel
/// Super Admin mapeia para todas as permissões do catálogo em
/// <see cref="EasyPanel.Modules.Identity.RolePermissions"/>, portanto satisfaz
/// naturalmente qualquer requisito de permissão.</para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionResolver _resolver;

    /// <summary>Cria o handler com o resolvedor de permissões injetado.</summary>
    public PermissionAuthorizationHandler(IPermissionResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (_resolver.HasPermission(context.User, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        // Não satisfazer o requisito leva o pipeline a negar (403) — R5.4.
        return Task.CompletedTask;
    }
}
