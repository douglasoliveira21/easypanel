using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Controllers.Auditing;

/// <summary>
/// Consulta da trilha de auditoria (R10.5): expõe
/// <c>GET /api/v1/audit-logs</c> paginado e restrito ao tenant do contexto
/// autenticado.
///
/// <para>
/// <b>Autorização (R5.3/R5.4).</b> Exige a permissão <c>audit.view</c> via
/// <see cref="RequirePermissionAttribute"/>: requisições não autenticadas recebem
/// 401 e autenticadas sem a permissão recebem 403, ambos pelo pipeline padrão de
/// autorização (avaliação sempre no backend — R5.6).
/// </para>
///
/// <para>
/// <b>Isolamento (R10.5).</b> A restrição por tenant é aplicada explicitamente pelo
/// <see cref="IAuditLogQueryService"/> a partir do <see cref="ITenantContext.TenantId"/>
/// — nunca de entrada do cliente. Um contexto sem tenant resolvido (ex.: Super
/// Admin sem tenant designado) recebe uma página vazia, evitando o vazamento de
/// eventos de plataforma ou de outros tenants.
/// </para>
///
/// <para>
/// <b>Paginação (R12.4).</b> <c>page</c> e <c>pageSize</c> são normalizados pelo
/// <see cref="PageRequest"/> (PageSize limitado a 100). Os resultados são ordenados
/// do evento mais recente ao mais antigo. A projeção usa <see cref="AuditLogDto"/>,
/// distinta da entidade (R12.1); os campos <c>OldValues</c>/<c>NewValues</c> já são
/// JSON redigido na origem (R11.2).
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(
    IAuditLogQueryService queryService,
    ITenantContext tenantContext) : ControllerBase
{
    private readonly IAuditLogQueryService _queryService = queryService;
    private readonly ITenantContext _tenantContext = tenantContext;

    /// <summary>
    /// Retorna uma página de eventos de auditoria do tenant do contexto (R10.5),
    /// ordenada por data decrescente. Requer a permissão <c>audit.view</c>.
    /// </summary>
    /// <param name="page">Número da página (base 1); normalizado para ≥ 1.</param>
    /// <param name="pageSize">Tamanho de página; limitado a 100 (R12.4).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    [HttpGet]
    [RequirePermission(Permissions.AuditView)]
    [ProducesResponseType(typeof(AuditLogPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditLogPageResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var request = new PageRequest(page, pageSize);

        var result = await _queryService
            .QueryAsync(_tenantContext.TenantId, request, cancellationToken)
            .ConfigureAwait(false);

        var items = result.Items.Select(ToDto).ToList();

        return Ok(new AuditLogPageResponse(items, result.Page, result.PageSize, result.TotalCount));
    }

    private static AuditLogDto ToDto(AuditLog log) =>
        new(
            log.Id,
            log.ActorUserId,
            log.Action,
            log.ResourceType,
            log.ResourceId,
            log.Result,
            log.OccurredAt,
            log.Ip,
            log.UserAgent,
            log.OldValues,
            log.NewValues);
}
