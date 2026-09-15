using Microsoft.AspNetCore.Identity;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Papel (role) da plataforma baseado em ASP.NET Core Identity com chave
/// <see cref="Guid"/>. Mínimo por ora; o catálogo de papéis e o mapeamento
/// papel→permissões são introduzidos pelas tarefas de RBAC (4.x).
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Cria um papel sem nome (exigido pelo Identity para materialização).</summary>
    public ApplicationRole()
    {
    }

    /// <summary>Cria um papel com o nome informado.</summary>
    public ApplicationRole(string roleName)
        : base(roleName)
    {
    }
}
