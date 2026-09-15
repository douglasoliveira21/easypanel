using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Gestão de usuários do tenant do contexto autenticado (R7): criação,
/// atualização (dados/papéis), desativação/reativação e listagem paginada. A
/// implementação orquestra o ASP.NET Core Identity
/// (<see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/>) e audita toda
/// mutação (R7.8), retornando sempre um <see cref="Result"/>/<see cref="Result{T}"/>
/// para que a camada de API mapeie falhas em códigos HTTP sem vazar dados
/// sensíveis.
///
/// <para><b>Origem do tenant (R6.3/R7.1).</b> Todo usuário é vinculado ao
/// <c>TenantId</c> do <see cref="EasyPanel.Modules.Tenancy.ITenantContext"/>, nunca
/// a um valor fornecido pelo chamador. Operações de leitura/escrita sobre um
/// usuário só são permitidas se ele pertencer ao tenant corrente; caso contrário,
/// o serviço responde <see cref="UserErrors.NotFound"/> (HTTP 404), sem distinguir
/// inexistência de acesso cross-tenant (R7.3, não-vazamento).</para>
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Cria um usuário vinculado ao tenant do contexto (R7.1). Define
    /// <c>UserName = Email</c>, <c>IsActive = true</c> e o momento de criação.
    /// Rejeita email duplicado no mesmo tenant com <see cref="UserErrors.DuplicateEmail"/>
    /// (HTTP 409 — R7.2), senha fora da política com <see cref="UserErrors.WeakPassword"/>
    /// e papéis desconhecidos com <see cref="UserErrors.UnknownRole"/> (HTTP 400).
    /// Sem tenant no contexto, retorna <see cref="UserErrors.NoTenantContext"/>. A
    /// criação bem-sucedida é auditada (R7.8), sem incluir a senha (R11.2).
    /// </summary>
    Task<Result<UserDto>> CreateAsync(CreateUserRequest req, CancellationToken ct);

    /// <summary>
    /// Atualiza dados e papéis de um usuário do próprio tenant (R7.3). Usuário
    /// inexistente ou de outro tenant retorna <see cref="UserErrors.NotFound"/>
    /// (HTTP 404). Reconcilia o conjunto de papéis (adiciona ausentes, remove
    /// excedentes) validando-os contra <see cref="Roles.All"/>. Audita a
    /// atualização e, quando os papéis mudam, também a alteração de papéis (R7.8).
    /// </summary>
    Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct);

    /// <summary>
    /// Desativa um usuário do próprio tenant (R7.4): marca <c>IsActive = false</c>,
    /// persiste e revoga todos os seus refresh tokens ativos
    /// (<see cref="ITokenService.RevokeAllForUserAsync"/>), impedindo a continuidade
    /// de sessões. Usuário inexistente/de outro tenant → <see cref="UserErrors.NotFound"/>.
    /// A desativação é auditada (R7.8).
    /// </summary>
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Reativa um usuário do próprio tenant (R7.6): marca <c>IsActive = true</c> e
    /// persiste. Usuário inexistente/de outro tenant → <see cref="UserErrors.NotFound"/>.
    /// A reativação é auditada (R7.8).
    /// </summary>
    Task<Result> ReactivateAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Lista os usuários do tenant do contexto (R7.7), em uma
    /// <see cref="PagedResult{T}"/> com <c>PageSize</c> já limitado a ≤ 100 (R12.4),
    /// ordenada de forma determinística. A restrição por tenant é explícita: usuários
    /// não recebem filtro global de ORM.
    /// </summary>
    Task<Result<PagedResult<UserDto>>> ListAsync(PageRequest query, CancellationToken ct);
}
