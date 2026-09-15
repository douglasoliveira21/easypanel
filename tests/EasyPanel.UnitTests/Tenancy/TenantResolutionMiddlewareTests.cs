using System.Security.Claims;

using EasyPanel.Modules.Tenancy;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.UnitTests.Tenancy;

/// <summary>
/// Unit tests para <see cref="TenantResolutionMiddleware"/>, cobrindo a
/// resolução do tenant a partir do <c>ClaimsPrincipal</c> (R6.3) e o tratamento
/// do endpoint administrativo designado para Super Admin (R6.7).
///
/// A origem do tenant é sempre o claim autenticado (usuários comuns) ou o
/// cabeçalho administrativo explícito (apenas Super Admin) — nunca corpo/query.
/// </summary>
public class TenantResolutionMiddlewareTests
{
    private static ServiceProvider BuildScopedProvider()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        return services.BuildServiceProvider();
    }

    private static async Task<TenantContext> RunAsync(
        ClaimsPrincipal? principal,
        Action<HttpContext>? configure = null)
    {
        var (tenantContext, _) = await RunFullAsync(principal, configure);
        return tenantContext;
    }

    private static async Task<(TenantContext Tenant, CustomerContext Customer)> RunFullAsync(
        ClaimsPrincipal? principal,
        Action<HttpContext>? configure = null)
    {
        await using var provider = BuildScopedProvider();
        using var scope = provider.CreateScope();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };

        if (principal is not null)
        {
            httpContext.User = principal;
        }

        configure?.Invoke(httpContext);

        var invoked = false;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.True(invoked, "O próximo middleware do pipeline deve sempre ser invocado.");

        return (
            scope.ServiceProvider.GetRequiredService<TenantContext>(),
            scope.ServiceProvider.GetRequiredService<CustomerContext>());
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task AuthenticatedUser_WithValidTenantClaim_ResolvesTenant()
    {
        var tenantId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(TenancyConstants.TenantIdClaimType, tenantId.ToString()));

        var context = await RunAsync(principal);

        Assert.Equal(tenantId, context.TenantId);
        Assert.True(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    [Fact]
    public async Task UnauthenticatedRequest_LeavesContextUnresolved()
    {
        // Identidade anônima (não autenticada), como uma requisição a /health.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var context = await RunAsync(anonymous);

        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    [Fact]
    public async Task NoPrincipal_LeavesContextUnresolved()
    {
        var context = await RunAsync(principal: null);

        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    [Fact]
    public async Task SuperAdmin_WithAdminTenantHeader_ResolvesTargetTenant()
    {
        var targetTenantId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(ClaimTypes.Role, TenancyConstants.SuperAdminRole));

        var context = await RunAsync(
            principal,
            ctx => ctx.Request.Headers[TenancyConstants.AdminTenantHeader] = targetTenantId.ToString());

        Assert.True(context.IsSuperAdmin);
        Assert.Equal(targetTenantId, context.TenantId);
        Assert.True(context.HasTenant);
    }

    [Fact]
    public async Task SuperAdmin_WithoutAdminTenantHeader_HasNoTenant()
    {
        var principal = AuthenticatedUser(
            new Claim(ClaimTypes.Role, TenancyConstants.SuperAdminRole));

        var context = await RunAsync(principal);

        Assert.True(context.IsSuperAdmin);
        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
    }

    [Fact]
    public async Task NonSuperAdmin_SupplyingAdminHeader_IgnoresHeader_AndUsesClaim()
    {
        var claimTenantId = Guid.NewGuid();
        var forgedTenantId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(TenancyConstants.TenantIdClaimType, claimTenantId.ToString()));

        var context = await RunAsync(
            principal,
            ctx => ctx.Request.Headers[TenancyConstants.AdminTenantHeader] = forgedTenantId.ToString());

        // O cabeçalho administrativo deve ser ignorado para não-Super Admin: o
        // tenant permanece o do claim autenticado (R6.7).
        Assert.Equal(claimTenantId, context.TenantId);
        Assert.False(context.IsSuperAdmin);
        Assert.NotEqual(forgedTenantId, context.TenantId);
    }

    [Fact]
    public async Task NonSuperAdmin_WithoutClaim_SupplyingAdminHeader_StaysUnresolved()
    {
        var forgedTenantId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(ClaimTypes.Name, "user@example.com"));

        var context = await RunAsync(
            principal,
            ctx => ctx.Request.Headers[TenancyConstants.AdminTenantHeader] = forgedTenantId.ToString());

        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    [Fact]
    public async Task AuthenticatedUser_WithNonGuidTenantClaim_StaysUnresolved()
    {
        var principal = AuthenticatedUser(
            new Claim(TenancyConstants.TenantIdClaimType, "not-a-guid"));

        var context = await RunAsync(principal);

        Assert.Null(context.TenantId);
        Assert.False(context.HasTenant);
        Assert.False(context.IsSuperAdmin);
    }

    // ---- Cliente (Fase 10 — Portal do Cliente, R2.2) -----------------------

    [Fact]
    public async Task ClienteUser_WithCustomerClaim_ResolvesCustomer()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(TenancyConstants.TenantIdClaimType, tenantId.ToString()),
            new Claim(TenancyConstants.CustomerIdClaimType, customerId.ToString()));

        var (tenant, customer) = await RunFullAsync(principal);

        Assert.Equal(tenantId, tenant.TenantId);
        Assert.Equal(customerId, customer.CustomerId);
        Assert.True(customer.HasCustomer);
    }

    [Fact]
    public async Task NonClienteUser_WithoutCustomerClaim_LeavesCustomerUnresolved()
    {
        var tenantId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(TenancyConstants.TenantIdClaimType, tenantId.ToString()));

        var (_, customer) = await RunFullAsync(principal);

        Assert.Null(customer.CustomerId);
        Assert.False(customer.HasCustomer);
    }

    [Fact]
    public async Task AuthenticatedUser_WithNonGuidCustomerClaim_LeavesCustomerUnresolved()
    {
        var tenantId = Guid.NewGuid();
        var principal = AuthenticatedUser(
            new Claim(TenancyConstants.TenantIdClaimType, tenantId.ToString()),
            new Claim(TenancyConstants.CustomerIdClaimType, "not-a-guid"));

        var (_, customer) = await RunFullAsync(principal);

        Assert.Null(customer.CustomerId);
        Assert.False(customer.HasCustomer);
    }
}
