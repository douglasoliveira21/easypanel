using EasyPanel.Modules.Tenancy;

namespace EasyPanel.UnitTests.Tenancy;

/// <summary>
/// Unit tests para <see cref="TenantContext"/>, cobrindo a resolução de
/// <see cref="ITenantContext.HasTenant"/> e o estado de Super Admin (R6.1, R6.3).
/// </summary>
public class TenantContextTests
{
    [Fact]
    public void NewContext_HasNoTenant_AndIsNotSuperAdmin()
    {
        var context = new TenantContext();

        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    [Fact]
    public void SetTenant_ResolvesTenant_AndHasTenantIsTrue()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext();

        context.SetTenant(tenantId);

        Assert.Equal(tenantId, context.TenantId);
        Assert.True(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    [Fact]
    public void SetSuperAdmin_WithoutTenant_HasNoTenant()
    {
        var context = new TenantContext();

        context.SetSuperAdmin();

        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
        Assert.True(context.IsSuperAdmin);
    }

    [Fact]
    public void SetSuperAdmin_WithTargetTenant_HasTenant()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext();

        context.SetSuperAdmin(tenantId);

        Assert.Equal(tenantId, context.TenantId);
        Assert.True(context.HasTenant);
        Assert.True(context.IsSuperAdmin);
    }
}
