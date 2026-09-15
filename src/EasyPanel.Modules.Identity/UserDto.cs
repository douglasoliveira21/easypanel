namespace EasyPanel.Modules.Identity;

/// <summary>
/// Projeção de leitura de um <see cref="ApplicationUser"/> exposta pelo
/// <see cref="IUserService"/> (R7). É um contrato de domínio distinto da entidade
/// de persistência: não expõe hash de senha, stamps nem quaisquer campos
/// sensíveis (R11.2). Os DTOs/validadores da camada de API (tarefa 6.2) podem
/// reutilizar este tipo ou mapeá-lo.
/// </summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Email">Email do usuário (também usado como nome de usuário).</param>
/// <param name="TenantId">Tenant ao qual o usuário pertence, quando houver.</param>
/// <param name="CustomerId">
/// Cliente (Customer) ao qual o usuário está vinculado (Fase 10), quando o papel
/// <see cref="Roles.Cliente"/> estiver atribuído; nulo caso contrário.
/// </param>
/// <param name="IsActive">Indica se o usuário está ativo e pode autenticar.</param>
/// <param name="MfaEnabled">Indica se o usuário exige segundo fator no login.</param>
/// <param name="Roles">Papéis atribuídos ao usuário.</param>
/// <param name="CreatedAt">Momento de criação do usuário, em UTC.</param>
public sealed record UserDto(
    Guid Id,
    string? Email,
    Guid? TenantId,
    Guid? CustomerId,
    bool IsActive,
    bool MfaEnabled,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);
