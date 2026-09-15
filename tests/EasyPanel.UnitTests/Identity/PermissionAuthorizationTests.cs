using System.Security.Claims;
using EasyPanel.Infrastructure.Security.Authorization;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace EasyPanel.UnitTests.Identity;

/// <summary>
/// Unit tests da avaliação de permissões (tarefa 4.2): o resolvedor em memória
/// (<see cref="RolePermissionResolver"/>) e o handler de autorização
/// (<see cref="PermissionAuthorizationHandler"/>).
///
/// Cobrem R5.3 (concede quando um papel do usuário tem a permissão), R5.4 (nega
/// — leva a 403 — quando nenhum papel concede) e a resolução por união de
/// múltiplos papéis. A avaliação é sempre a partir dos papéis do
/// <see cref="ClaimsPrincipal"/> (R5.6).
/// </summary>
public class PermissionAuthorizationTests
{
    /// <summary>Nome do claim de papel usado no token (ver TokenService.RolesClaimType).</summary>
    private const string RolesClaimType = "roles";

    private static readonly RolePermissionResolver Resolver = new();

    // ---- Resolver: união por papel ----------------------------------------

    [Fact]
    public void ResolvePermissions_ForSingleRole_ReturnsThatRolesPermissions()
    {
        var permissions = Resolver.ResolvePermissions(new[] { Roles.Operacional });

        Assert.Contains(Permissions.CustomerCreate, permissions);
        Assert.Contains(Permissions.LocationCreate, permissions);
        // Operacional não gerencia usuários.
        Assert.DoesNotContain(Permissions.UserManage, permissions);
    }

    [Fact]
    public void ResolvePermissions_UnionsAcrossMultipleRoles()
    {
        // Financeiro (leitura + auditoria) ∪ Operacional (CRUD de negócio).
        var permissions = Resolver.ResolvePermissions(new[] { Roles.Financeiro, Roles.Operacional });

        // Da união: create/edit vêm do Operacional; audit.view vem do Financeiro.
        Assert.Contains(Permissions.CustomerCreate, permissions);
        Assert.Contains(Permissions.CustomerEdit, permissions);
        Assert.Contains(Permissions.AuditView, permissions);
        // Nenhum dos dois concede gestão de usuários.
        Assert.DoesNotContain(Permissions.UserManage, permissions);
    }

    [Fact]
    public void ResolvePermissions_IgnoresUnknownAndBlankRoles()
    {
        var permissions = Resolver.ResolvePermissions(new[] { "PapelInexistente", "", Roles.Supervisor });

        Assert.Contains(Permissions.AuditView, permissions);
        Assert.DoesNotContain(Permissions.UserManage, permissions);
    }

    [Fact]
    public void ResolvePermissions_ForSuperAdmin_ReturnsEntireCatalog()
    {
        var permissions = Resolver.ResolvePermissions(new[] { Roles.SuperAdmin });

        foreach (var permission in Permissions.All)
        {
            Assert.Contains(permission, permissions);
        }
    }

    // ---- Resolver: HasPermission a partir do principal ---------------------

    [Fact]
    public void HasPermission_UnauthenticatedPrincipal_IsFalse()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(Resolver.HasPermission(anonymous, Permissions.CustomerView));
    }

    // ---- Handler: concede por papel (R5.3) --------------------------------

    [Fact]
    public async Task Handler_WithRoleGrantingPermission_Succeeds()
    {
        var user = PrincipalWithRoles(Roles.Operacional);
        var context = Evaluate(user, Permissions.CustomerCreate);

        await HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    // ---- Handler: nega quando papel não concede (R5.4) --------------------

    [Fact]
    public async Task Handler_WithRoleLackingPermission_DoesNotSucceed()
    {
        // Operacional não possui user.manage → não satisfaz → 403 no pipeline.
        var user = PrincipalWithRoles(Roles.Operacional);
        var context = Evaluate(user, Permissions.UserManage);

        await HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_WithNoRoles_DoesNotSucceed()
    {
        var user = PrincipalWithRoles();
        var context = Evaluate(user, Permissions.CustomerView);

        await HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    // ---- Handler: Super Admin satisfaz qualquer permissão -----------------

    [Fact]
    public async Task Handler_ForSuperAdmin_SucceedsForAnyPermission()
    {
        var user = PrincipalWithRoles(Roles.SuperAdmin);

        foreach (var permission in Permissions.All)
        {
            var context = Evaluate(user, permission);
            await HandleAsync(context);
            Assert.True(context.HasSucceeded, $"Super Admin deveria conceder {permission}");
        }
    }

    // ---- Handler: união de papéis satisfaz o requisito --------------------

    [Fact]
    public async Task Handler_WithMultipleRoles_SucceedsWhenAnyGrantsPermission()
    {
        // Estoque (só leitura) + Administrador (gestão) → concede user.manage.
        var user = PrincipalWithRoles(Roles.Estoque, Roles.Administrador);
        var context = Evaluate(user, Permissions.UserManage);

        await HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task HandleAsync(AuthorizationHandlerContext context)
    {
        var handler = new PermissionAuthorizationHandler(Resolver);
        await handler.HandleAsync(context);
    }

    private static AuthorizationHandlerContext Evaluate(ClaimsPrincipal user, string permission)
    {
        var requirement = new PermissionRequirement(permission);
        return new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
    }

    /// <summary>
    /// Constrói um principal autenticado cujos papéis são expostos no claim
    /// "roles" — o mesmo claim de papel configurado no esquema Bearer
    /// (RoleClaimType = "roles"), garantindo que <c>IsInRole</c> funcione.
    /// </summary>
    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(RolesClaimType, r));
        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: "sub",
            roleType: RolesClaimType);

        return new ClaimsPrincipal(identity);
    }
}
