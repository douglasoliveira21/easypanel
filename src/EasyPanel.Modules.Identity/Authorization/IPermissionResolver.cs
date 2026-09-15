using System.Security.Claims;

namespace EasyPanel.Modules.Identity.Authorization;

/// <summary>
/// Resolve o conjunto de permissões efetivas de um usuário a partir dos seus
/// papéis (R5.3). As permissões <b>não</b> são embutidas no JWT (ver
/// <see cref="ITokenService"/>): são derivadas dos papéis do usuário a cada
/// avaliação de autorização, de modo que uma mudança de atribuição de papéis
/// reflita na próxima avaliação (R5.5) sem reemissão de token.
///
/// <para>A implementação padrão resolve a partir do mapeamento em código
/// (<see cref="RolePermissions"/>), a fonte única de verdade da Fase 1. O contrato
/// é desenhado para permitir, numa fase futura, uma implementação com cache
/// (ex.: Redis) quando o mapeamento papel→permissões passar a ser dinâmico/
/// persistido — sem alterar os consumidores.</para>
/// </summary>
public interface IPermissionResolver
{
    /// <summary>
    /// Retorna a união das permissões concedidas pelos <paramref name="roleNames"/>
    /// informados. Papéis desconhecidos contribuem com o conjunto vazio; a coleção
    /// resultante não contém duplicatas.
    /// </summary>
    IReadOnlySet<string> ResolvePermissions(IEnumerable<string> roleNames);

    /// <summary>
    /// Determina se o <paramref name="principal"/> autenticado possui a
    /// <paramref name="permission"/> exigida, a partir dos papéis presentes no seu
    /// <see cref="ClaimsPrincipal"/>. Um principal não autenticado nunca possui
    /// permissões.
    /// </summary>
    bool HasPermission(ClaimsPrincipal principal, string permission);
}
