using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;

namespace EasyPanel.UnitTests.Identity;

/// <summary>
/// Unit tests do catálogo de RBAC da Fase 1 (tarefa 4.1): papéis (<see cref="Roles"/>),
/// permissões (<see cref="Permissions"/>) e o mapeamento papel→permissões
/// (<see cref="RolePermissions"/>).
///
/// Cobrem R5.1 (os 8 papéis existem) e R5.2 (cada papel tem um conjunto de
/// permissões granulares consistente com o catálogo).
/// </summary>
public class RbacCatalogTests
{
    /// <summary>R5.1: os 8 papéis fixos da Fase 1 estão presentes.</summary>
    [Fact]
    public void Roles_ContainsAllEightPhaseOneRoles()
    {
        var expected = new[]
        {
            "Super Admin",
            "Administrador",
            "Financeiro",
            "Operacional",
            "Estoque",
            "Técnico",
            "Supervisor",
            "Cliente",
        };

        Assert.Equal(8, Roles.All.Count);
        Assert.Equal(expected.OrderBy(r => r, StringComparer.Ordinal),
            Roles.All.OrderBy(r => r, StringComparer.Ordinal));
    }

    /// <summary>
    /// O nome do papel Super Admin deve ser idêntico ao usado pela resolução de
    /// tenant (<c>IsInRole("Super Admin")</c>), evitando divergência (R6.7).
    /// </summary>
    [Fact]
    public void SuperAdminRole_MatchesTenancyConstant()
    {
        Assert.Equal(TenancyConstants.SuperAdminRole, Roles.SuperAdmin);
    }

    /// <summary>R5.2: o mapeamento cobre exatamente os 8 papéis.</summary>
    [Fact]
    public void RolePermissions_MapsAllEightRoles()
    {
        Assert.Equal(8, RolePermissions.Map.Count);
        foreach (var role in Roles.All)
        {
            Assert.True(RolePermissions.Map.ContainsKey(role), $"Papel ausente no mapeamento: {role}");
        }
    }

    /// <summary>Super Admin concede todas as permissões do catálogo.</summary>
    [Fact]
    public void SuperAdmin_HasAllPermissions()
    {
        var superAdmin = RolePermissions.ForRole(Roles.SuperAdmin);

        foreach (var permission in Permissions.All)
        {
            Assert.Contains(permission, superAdmin);
        }

        Assert.Equal(Permissions.All.Count, superAdmin.Count);
    }

    /// <summary>
    /// Administrador gerencia usuários, Clientes e Locais e visualiza auditoria
    /// (controle total dentro do tenant).
    /// </summary>
    [Fact]
    public void Administrador_HasTenantScopedManagementPermissions()
    {
        var admin = RolePermissions.ForRole(Roles.Administrador);

        Assert.Contains(Permissions.UserManage, admin);
        Assert.Contains(Permissions.CustomerView, admin);
        Assert.Contains(Permissions.CustomerCreate, admin);
        Assert.Contains(Permissions.CustomerEdit, admin);
        Assert.Contains(Permissions.CustomerDelete, admin);
        Assert.Contains(Permissions.LocationView, admin);
        Assert.Contains(Permissions.LocationCreate, admin);
        Assert.Contains(Permissions.LocationEdit, admin);
        Assert.Contains(Permissions.LocationDelete, admin);
        Assert.Contains(Permissions.AuditView, admin);
    }

    /// <summary>
    /// Toda permissão referenciada em qualquer papel existe no catálogo
    /// <see cref="Permissions.All"/> (sem erros de digitação nem órfãs).
    /// </summary>
    [Fact]
    public void EveryMappedPermission_ExistsInCatalog()
    {
        foreach (var (role, permissions) in RolePermissions.Map)
        {
            foreach (var permission in permissions)
            {
                Assert.True(
                    Permissions.All.Contains(permission),
                    $"Permissão '{permission}' do papel '{role}' não existe em Permissions.All");
            }
        }
    }

    /// <summary>
    /// Cliente recebe exclusivamente as permissões do Portal do Cliente
    /// (Fase 10), nenhuma permissão administrativa/operacional do tenant.
    /// </summary>
    [Fact]
    public void Cliente_HasOnlyPortalPermissions()
    {
        var permissions = RolePermissions.ForRole(Roles.Cliente);

        Assert.Equal(
            new[] { Permissions.PortalChamadoView, Permissions.PortalFaturaView, Permissions.PortalParqueView },
            permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>Papel desconhecido resolve para um conjunto vazio (sem exceção).</summary>
    [Fact]
    public void ForRole_UnknownRole_ReturnsEmpty()
    {
        Assert.Empty(RolePermissions.ForRole("PapelInexistente"));
    }

    /// <summary>O catálogo não contém permissões duplicadas.</summary>
    [Fact]
    public void Permissions_CatalogHasNoDuplicates()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// R17.1 (Fase 2): as permissões de monitoramento (impressoras, contadores e
    /// agentes) estão presentes no catálogo.
    /// </summary>
    [Fact]
    public void Permissions_CatalogHasPhaseTwoMonitoringPermissions()
    {
        var phaseTwo = new[]
        {
            Permissions.PrinterView,
            Permissions.PrinterCreate,
            Permissions.PrinterEdit,
            Permissions.PrinterMove,
            Permissions.PrinterMonitor,
            Permissions.CounterView,
            Permissions.CounterAdjust,
            Permissions.ClientView,
            Permissions.ClientManage,
        };

        foreach (var permission in phaseTwo)
        {
            Assert.Contains(permission, Permissions.All);
        }
    }
}
