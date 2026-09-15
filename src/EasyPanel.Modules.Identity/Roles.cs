using EasyPanel.Modules.Tenancy;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Papéis (roles) fixos da Fase 1 (R5.1). São propositalmente um conjunto fechado
/// e semeado no banco (ver o seeder de papéis na Infrastructure); a atribuição de
/// papéis a usuários e o mapeamento papel→permissões vivem, respectivamente, na
/// gestão de usuários e em <see cref="RolePermissions"/>.
/// </summary>
public static class Roles
{
    /// <summary>
    /// Papel com acesso global à plataforma (opera entre tenants em endpoints
    /// administrativos designados). O nome deve ser idêntico ao
    /// <see cref="TenancyConstants.SuperAdminRole"/> para que a resolução de tenant
    /// (<c>IsInRole("Super Admin")</c>) funcione — por isso referenciamos a
    /// constante em vez de duplicar o literal.
    /// </summary>
    public const string SuperAdmin = TenancyConstants.SuperAdminRole;

    /// <summary>Administrador do tenant: controle total dentro do próprio tenant.</summary>
    public const string Administrador = "Administrador";

    /// <summary>Perfil financeiro (contratos/fechamento em fases futuras).</summary>
    public const string Financeiro = "Financeiro";

    /// <summary>Perfil operacional: cadastro/manutenção de Clientes e Locais.</summary>
    public const string Operacional = "Operacional";

    /// <summary>Perfil de estoque/suprimentos (relevante em fases futuras).</summary>
    public const string Estoque = "Estoque";

    /// <summary>Perfil técnico (chamados/SLA em fases futuras).</summary>
    public const string Tecnico = "Técnico";

    /// <summary>Perfil de supervisão: visão e acompanhamento, incluindo auditoria.</summary>
    public const string Supervisor = "Supervisor";

    /// <summary>Perfil do portal do cliente (fase futura); sem permissões
    /// administrativas na Fase 1.</summary>
    public const string Cliente = "Cliente";

    /// <summary>
    /// Conjunto imutável de todos os 8 papéis da Fase 1 (R5.1). Lista explícita e
    /// determinística usada pelo seeder e pelos testes.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        SuperAdmin,
        Administrador,
        Financeiro,
        Operacional,
        Estoque,
        Tecnico,
        Supervisor,
        Cliente,
    };
}
