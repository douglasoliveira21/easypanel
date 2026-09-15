using EasyPanel.Modules.Identity;

namespace EasyPanel.Api.Controllers.Users;

/// <summary>
/// Corpo da requisição de criação de usuário na camada de API (R7.1, R12.1). É
/// um contrato de API distinto do <see cref="CreateUserRequest"/> de domínio: o
/// controller o valida (via FluentValidation — R12.2) e o mapeia para o record de
/// domínio antes de chamar o <see cref="IUserService"/>. O tenant <b>não</b> é
/// informado aqui — é derivado do contexto autenticado (R6.3/R7.1).
/// </summary>
/// <param name="Email">Email do usuário (único por tenant — R7.2).</param>
/// <param name="Password">Senha em texto claro (política R3.7); nunca logada (R11.2).</param>
/// <param name="Roles">Papéis a atribuir, do catálogo <see cref="Roles.All"/>.</param>
/// <param name="CustomerId">
/// Cliente (Customer) ao qual vincular o usuário (Fase 10 — Portal do Cliente).
/// Obrigatório quando <paramref name="Roles"/> contém <see cref="Roles.Cliente"/>.
/// </param>
public sealed record CreateUserApiRequest(
    string Email,
    string Password,
    IReadOnlyList<string>? Roles,
    Guid? CustomerId = null);

/// <summary>
/// Corpo da requisição de atualização de usuário na camada de API (R7.3, R12.1).
/// Contrato distinto do <see cref="UpdateUserRequest"/> de domínio. Não inclui
/// senha (conduzida pelos fluxos de senha do <see cref="IAuthService"/>) nem o
/// tenant (imutável).
/// </summary>
/// <param name="Email">Novo email/nome de usuário (único por tenant — R7.2).</param>
/// <param name="MfaEnabled">Novo estado de exigência de segundo fator (R2.10).</param>
/// <param name="Roles">Conjunto desejado de papéis, do catálogo <see cref="Roles.All"/>.</param>
/// <param name="CustomerId">
/// Cliente (Customer) ao qual vincular o usuário (Fase 10 — Portal do Cliente).
/// Obrigatório quando <paramref name="Roles"/> contém <see cref="Roles.Cliente"/>.
/// </param>
public sealed record UpdateUserApiRequest(
    string Email,
    bool MfaEnabled,
    IReadOnlyList<string>? Roles,
    Guid? CustomerId = null);

/// <summary>
/// Projeção de saída de um usuário na camada de API (R12.1). Espelha o
/// <see cref="UserDto"/> de domínio em um contrato de API estável; jamais expõe
/// hash de senha, stamps de segurança ou quaisquer campos sensíveis (R11.2).
/// </summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Email">Email do usuário.</param>
/// <param name="TenantId">Tenant ao qual o usuário pertence, quando houver.</param>
/// <param name="CustomerId">
/// Cliente (Customer) ao qual o usuário está vinculado (Fase 10), quando houver.
/// </param>
/// <param name="IsActive">Indica se o usuário está ativo e pode autenticar.</param>
/// <param name="MfaEnabled">Indica se o usuário exige segundo fator no login.</param>
/// <param name="Roles">Papéis atribuídos ao usuário.</param>
/// <param name="CreatedAt">Momento de criação do usuário, em UTC.</param>
public sealed record UserResponse(
    Guid Id,
    string? Email,
    Guid? TenantId,
    Guid? CustomerId,
    bool IsActive,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);

/// <summary>
/// Página de resultados da listagem de usuários (R7.7/R12.4). Espelha um
/// <see cref="EasyPanel.Shared.Kernel.Pagination.PagedResult{T}"/> de
/// <see cref="UserResponse"/> em um contrato de API estável.
/// </summary>
/// <param name="Items">Usuários da página atual.</param>
/// <param name="Page">Número da página (base 1).</param>
/// <param name="PageSize">Tamanho de página efetivamente aplicado (≤ 100 — R12.4).</param>
/// <param name="TotalCount">Total de usuários do tenant, ignorando a paginação.</param>
public sealed record UserPageResponse(
    IReadOnlyList<UserResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
