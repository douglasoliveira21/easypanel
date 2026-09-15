namespace EasyPanel.Modules.Identity;

/// <summary>
/// Dados de entrada para a criação de um usuário (R7.1, R7.2). O tenant do novo
/// usuário <b>não</b> é informado aqui: é sempre derivado do contexto autenticado
/// pelo <see cref="IUserService"/> (R6.3/R7.1), nunca do chamador.
/// </summary>
/// <param name="Email">
/// Email do usuário; também usado como nome de usuário (unicidade por tenant via
/// índice composto <c>(TenantId, NormalizedEmail)</c> — R7.2).
/// </param>
/// <param name="Password">
/// Senha em texto claro, sujeita à política de senhas do Identity (R3.7). É
/// hasheada (PBKDF2) pelo <c>UserManager</c> e jamais persistida/auditada em
/// texto claro (R2.8/R11.2).
/// </param>
/// <param name="Roles">Papéis a atribuir; devem pertencer ao catálogo <see cref="Roles.All"/>.</param>
/// <param name="CustomerId">
/// Cliente (Customer) ao qual vincular o usuário (Fase 10). Obrigatório quando
/// <paramref name="Roles"/> contém <see cref="Roles.Cliente"/>; ignorado (forçado a
/// nulo) caso contrário.
/// </param>
public sealed record CreateUserRequest(
    string Email,
    string Password,
    IReadOnlyList<string> Roles,
    Guid? CustomerId = null);
