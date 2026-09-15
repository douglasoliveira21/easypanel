using System.Security.Claims;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Identity.Authorization;

namespace EasyPanel.Infrastructure.Security.Authorization;

/// <summary>
/// Implementação de <see cref="IPermissionResolver"/> que resolve as permissões
/// efetivas de um usuário <b>em memória</b>, a partir do mapeamento em código
/// <see cref="RolePermissions"/> (fonte única de verdade da Fase 1 — R5.3).
///
/// <para><b>Decisão de projeto — em memória, sem Redis na Fase 1.</b> O design
/// menciona cachear as permissões por papel no Redis. Na Fase 1, porém, o
/// mapeamento papel→permissões é <i>estático em código</i>: um cache distribuído
/// não traria ganho de performance relevante (a resolução é apenas uma união de
/// conjuntos em memória) e introduziria uma dependência dura do Redis no caminho
/// crítico de autorização — um ponto de falha capaz de bloquear toda requisição
/// autorizada durante uma indisponibilidade do cache. Optou-se, portanto, por uma
/// resolução puramente em memória (rápida, sem I/O, sem ponto de falha adicional).
/// </para>
///
/// <para><b>Costura para evolução futura.</b> Quando o mapeamento
/// papel→permissões passar a ser dinâmico/persistido (fase futura, ex.: papéis e
/// permissões editáveis por tenant), um <see cref="IPermissionResolver"/>
/// alternativo com cache no Redis pode ser introduzido <i>sem</i> alterar os
/// consumidores (handler/policy provider). Se/quando isso ocorrer, esse cache
/// deverá <b>falhar aberto</b> para o mapeamento em código quando o Redis estiver
/// indisponível, nunca bloqueando a autorização por uma falha de cache.</para>
///
/// <para><b>Reflexo de mudança de papel (R5.5).</b> Como as permissões são
/// derivadas dos papéis presentes no <see cref="ClaimsPrincipal"/> a cada
/// requisição — e não de uma lista de permissões embutida no token — a alteração
/// dos papéis de um usuário reflete-se na próxima avaliação de autorização (após
/// o usuário obter um token que reflita seus papéis atuais). O cache (quando
/// existir) é chaveado por <i>papel</i>, não por usuário, preservando essa
/// propriedade.</para>
/// </summary>
public sealed class RolePermissionResolver : IPermissionResolver
{
    /// <inheritdoc />
    public IReadOnlySet<string> ResolvePermissions(IEnumerable<string> roleNames)
    {
        ArgumentNullException.ThrowIfNull(roleNames);

        var permissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var roleName in roleNames)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                continue;
            }

            // União das permissões do papel (conjunto vazio para papel desconhecido).
            foreach (var permission in RolePermissions.ForRole(roleName))
            {
                permissions.Add(permission);
            }
        }

        return permissions;
    }

    /// <inheritdoc />
    public bool HasPermission(ClaimsPrincipal principal, string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (string.IsNullOrWhiteSpace(permission)
            || principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        // Coleta os papéis do usuário de forma robusta quanto à serialização dos
        // claims: com RoleClaimType = "roles" e um array JSON no token, os papéis
        // podem aparecer como múltiplos claims OU como um único claim contendo um
        // array. Iterar o conjunto fechado de papéis conhecidos (Roles.All) e usar
        // ClaimsPrincipal.IsInRole é indiferente a essa diferença de serialização
        // e evita depender do formato exato do claim.
        foreach (var roleName in Roles.All)
        {
            if (!principal.IsInRole(roleName))
            {
                continue;
            }

            if (RolePermissions.ForRole(roleName).Contains(permission))
            {
                return true;
            }
        }

        return false;
    }
}
