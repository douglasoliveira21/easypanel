using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Erros esperados da gestão de usuários (R7), expostos como valores estáveis
/// para uso pelo <see cref="IUserService"/> e mapeamento na camada de API. Cada
/// erro carrega uma <see cref="ErrorType"/> que a camada de API traduz em HTTP
/// (400/404/409) — ver a seção Error Handling do design.
/// </summary>
public static class UserErrors
{
    /// <summary>
    /// Não há tenant resolvido no contexto para vincular o novo usuário (R7.1).
    /// A criação exige um tenant autenticado; sem ele, não é possível persistir o
    /// usuário. Mapeado para HTTP 400 (validação).
    /// </summary>
    public static readonly Error NoTenantContext = Error.Validation(
        "user.tenant.missing",
        "Não há tenant no contexto para criar o usuário.");

    /// <summary>
    /// Já existe um usuário com o mesmo email no tenant (R7.2). Mapeado para HTTP 409.
    /// </summary>
    public static readonly Error DuplicateEmail = Error.Conflict(
        "user.email.duplicate",
        "Já existe um usuário com este email neste tenant.");

    /// <summary>
    /// Um ou mais papéis informados não pertencem ao catálogo <see cref="Roles.All"/>
    /// (R5.1). Mapeado para HTTP 400 (validação).
    /// </summary>
    public static readonly Error UnknownRole = Error.Validation(
        "user.role.unknown",
        "Um ou mais papéis informados são inválidos.");

    /// <summary>
    /// A senha informada não atende à política de senhas (R3.7). Mapeado para HTTP 400.
    /// </summary>
    public static readonly Error WeakPassword = Error.Validation(
        "user.password.weak",
        "A senha não atende à política de senhas.");

    /// <summary>
    /// Falha de validação genérica reportada pelo Identity ao criar/atualizar o
    /// usuário (ex.: email inválido). Mapeado para HTTP 400.
    /// </summary>
    public static readonly Error InvalidData = Error.Validation(
        "user.data.invalid",
        "Os dados informados para o usuário são inválidos.");

    /// <summary>
    /// Usuário inexistente ou pertencente a outro tenant (R7.3). Retornado como
    /// <see cref="ErrorType.NotFound"/> (HTTP 404) para não revelar a existência
    /// de usuários de outros tenants (não-vazamento).
    /// </summary>
    public static readonly Error NotFound = Error.NotFound(
        "user.not_found",
        "Usuário não encontrado.");

    /// <summary>
    /// O papel <see cref="Roles.Cliente"/> foi atribuído sem um <c>CustomerId</c>
    /// (Fase 10 — R1.1). Mapeado para HTTP 400 (validação).
    /// </summary>
    public static readonly Error CustomerRequired = Error.Validation(
        "user.customer.required",
        "É necessário informar o Cliente ao atribuir o papel Cliente.");

    /// <summary>
    /// O <c>CustomerId</c> informado não existe no tenant corrente (Fase 10 —
    /// R1.1). Retornado como <see cref="ErrorType.Validation"/> (HTTP 400), sem
    /// distinguir inexistência de pertencimento a outro tenant (não-vazamento).
    /// </summary>
    public static readonly Error CustomerNotFound = Error.Validation(
        "user.customer.not_found",
        "O Cliente informado não foi encontrado neste tenant.");

    /// <summary>
    /// O papel <see cref="Roles.Cliente"/> foi combinado com qualquer outro papel
    /// (Fase 10 — R1.2). Mapeado para HTTP 400 (validação).
    /// </summary>
    public static readonly Error RoleConflict = Error.Validation(
        "user.role.conflict",
        "O papel Cliente não pode ser combinado com outros papéis.");
}
