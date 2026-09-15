using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.IntegrationTests.Authorization;

/// <summary>
/// Controller <b>exclusivo de teste</b> usado para exercitar a autorização por
/// permissão (tarefa 4.2 / R5.3–R5.6) de ponta a ponta, sem depender dos
/// controllers de negócio (Usuários/Clientes/Locais — tarefas 6–8).
///
/// Registrado como <c>ApplicationPart</c> apenas pela factory de teste
/// (<see cref="AuthorizationWebApplicationFactory"/>); não faz parte da superfície
/// de produção. Cada endpoint devolve <c>204 No Content</c> quando autorizado,
/// permitindo distinguir claramente 200/204 (permitido) de 403 (sem permissão) e
/// 401 (não autenticado).
/// </summary>
[ApiController]
[Route("api/v1/_authz-probe")]
public sealed class AuthzProbeController : ControllerBase
{
    /// <summary>Requer a permissão <c>audit.view</c> (R5.3/R5.4).</summary>
    [HttpGet("audit")]
    [RequirePermission(Permissions.AuditView)]
    public IActionResult AuditView() => NoContent();

    /// <summary>Requer a permissão <c>user.manage</c> (permissão administrativa).</summary>
    [HttpGet("user-manage")]
    [RequirePermission(Permissions.UserManage)]
    public IActionResult UserManage() => NoContent();

    /// <summary>Requer a permissão <c>customer.view</c> (permissão de leitura comum).</summary>
    [HttpGet("customer-view")]
    [RequirePermission(Permissions.CustomerView)]
    public IActionResult CustomerView() => NoContent();

    /// <summary>Apenas autenticação, sem permissão específica (baseline de 401 vs 204).</summary>
    [HttpGet("authenticated")]
    [Authorize]
    public IActionResult AuthenticatedOnly() => NoContent();
}
