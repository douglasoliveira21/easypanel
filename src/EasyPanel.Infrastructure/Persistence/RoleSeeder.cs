using EasyPanel.Modules.Identity;
using Microsoft.AspNetCore.Identity;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Implementação de <see cref="IRoleSeeder"/> sobre o <see cref="RoleManager{TRole}"/>
/// do ASP.NET Core Identity. Semeia os papéis de <see cref="Roles.All"/> (R5.1),
/// criando apenas os ausentes — a operação é idempotente.
///
/// Não persiste permissões: o mapeamento papel→permissões (<see cref="RolePermissions"/>)
/// é resolvido em código pela avaliação de autorização (tarefa 4.2). Assim, 4.1
/// limita-se a garantir a existência dos papéis.
/// </summary>
public sealed class RoleSeeder : IRoleSeeder
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    /// <summary>Cria o seeder com o <see cref="RoleManager{TRole}"/> injetado.</summary>
    public RoleSeeder(RoleManager<ApplicationRole> roleManager)
    {
        ArgumentNullException.ThrowIfNull(roleManager);
        _roleManager = roleManager;
    }

    /// <inheritdoc />
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var created = 0;

        foreach (var roleName in Roles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
            {
                continue;
            }

            var result = await _roleManager
                .CreateAsync(new ApplicationRole(roleName))
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                // Corrida entre instâncias: se outro processo já criou o papel,
                // considera idempotente; caso contrário, propaga o erro.
                if (await _roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
                {
                    continue;
                }

                var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                throw new InvalidOperationException(
                    $"Falha ao semear o papel '{roleName}': {errors}");
            }

            created++;
        }

        return created;
    }
}
