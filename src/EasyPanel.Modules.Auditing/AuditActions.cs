namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Ações canônicas da trilha de auditoria (R10.1), no formato <c>recurso.acao</c>.
/// Centralizadas para que emissores e consumidores (consultas, testes) usem
/// exatamente as mesmas cadeias, evitando divergências por digitação.
/// </summary>
public static class AuditActions
{
    /// <summary>
    /// Evento de login (sucesso ou falha). O desfecho é discriminado pelo
    /// <see cref="AuditResult"/> do registro (R4.1, R10.2).
    /// </summary>
    public const string AuthLogin = "auth.login";

    /// <summary>Tipo de recurso associado a eventos de autenticação.</summary>
    public const string AuthResourceType = "Auth";

    /// <summary>Criação de um usuário (R7.1, R7.8).</summary>
    public const string UserCreate = "user.create";

    /// <summary>Atualização de dados de um usuário (R7.3, R7.8).</summary>
    public const string UserUpdate = "user.update";

    /// <summary>Alteração do conjunto de papéis de um usuário (R7.3, R7.8).</summary>
    public const string UserRolesUpdate = "user.roles.update";

    /// <summary>Desativação de um usuário (R7.4, R7.8).</summary>
    public const string UserDeactivate = "user.deactivate";

    /// <summary>Reativação de um usuário (R7.6, R7.8).</summary>
    public const string UserReactivate = "user.reactivate";

    /// <summary>Tipo de recurso associado a eventos de gestão de usuários.</summary>
    public const string UserResourceType = "User";
}
