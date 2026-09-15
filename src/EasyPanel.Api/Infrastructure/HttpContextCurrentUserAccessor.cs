using System.Security.Claims;
using EasyPanel.Modules.Identity;

namespace EasyPanel.Api.Infrastructure;

/// <summary>
/// Implementação de <see cref="ICurrentUserAccessor"/> baseada no
/// <see cref="IHttpContextAccessor"/>: resolve o identificador do usuário atuante
/// a partir do claim <c>sub</c> do <see cref="ClaimsPrincipal"/> da requisição
/// corrente (R6.3), para enriquecer a trilha de auditoria com o ator das
/// mutações (R7.8).
///
/// <para>O nome curto <c>sub</c> é preservado porque o esquema Bearer usa
/// <c>MapInboundClaims = false</c> (mesma convenção do <c>AuthController</c>).
/// Quando não há requisição corrente ou o claim é ausente/inválido, devolve
/// <c>null</c> graciosamente — a auditoria segue funcionando sem ator.</para>
/// </summary>
public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserAccessor
{
    /// <summary>
    /// Nome curto do claim de subject (Id do usuário) embutido no JWT, idêntico ao
    /// usado pelo <c>AuthController</c>.
    /// </summary>
    private const string SubClaimType = "sub";

    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            var subject = _httpContextAccessor.HttpContext?.User.FindFirstValue(SubClaimType);
            return Guid.TryParse(subject, out var userId) ? userId : null;
        }
    }
}
