using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.IntegrationTests.Authorization;

/// <summary>
/// Testes de integração/segurança da autorização por permissão (tarefa 4.2 /
/// R5.3, R5.4, R5.6). Exercitam o pipeline HTTP completo (autenticação Bearer +
/// policy provider dinâmico <c>perm:&lt;permission&gt;</c> + handler) sobre o
/// <see cref="AuthzProbeController"/>, provando 204 (permitido) vs 403 (sem
/// permissão) vs 401 (não autenticado). A avaliação ocorre sempre no backend a
/// partir dos papéis do token, independentemente do frontend (R5.6).
/// </summary>
public sealed class PermissionAuthorizationEndpointTests
{
    private static AuthorizationWebApplicationFactory CreateFactory() => new();

    // ---- 401: sem autenticação --------------------------------------------

    [Fact]
    public async Task ProtectedEndpoint_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/_authz-probe/customer-view");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- 403: autenticado, sem a permissão exigida (R5.4) -----------------

    [Fact]
    public async Task UserManageEndpoint_AsOperational_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.OperationalEmail);

        // Operacional não possui user.manage → 403 (R5.4).
        using var response = await client.GetAsync("/api/v1/_authz-probe/user-manage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditEndpoint_AsOperational_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.OperationalEmail);

        // Operacional não possui audit.view → 403.
        using var response = await client.GetAsync("/api/v1/_authz-probe/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnyPermissionEndpoint_AsClient_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.ClientEmail);

        // Cliente não possui permissões administrativas na Fase 1 → 403.
        using var response = await client.GetAsync("/api/v1/_authz-probe/customer-view");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- 204: autenticado, com a permissão exigida (R5.3) -----------------

    [Fact]
    public async Task CustomerViewEndpoint_AsOperational_ReturnsNoContent()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.OperationalEmail);

        // Operacional possui customer.view → 204 (R5.3).
        using var response = await client.GetAsync("/api/v1/_authz-probe/customer-view");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task UserManageEndpoint_AsAdministrator_ReturnsNoContent()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.AdminEmail);

        // Administrador possui user.manage → 204 (R5.3).
        using var response = await client.GetAsync("/api/v1/_authz-probe/user-manage");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AuditEndpoint_AsAdministrator_ReturnsNoContent()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.AdminEmail);

        using var response = await client.GetAsync("/api/v1/_authz-probe/audit");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- Super Admin passa em qualquer permissão --------------------------

    [Fact]
    public async Task AllProtectedEndpoints_AsSuperAdmin_ReturnNoContent()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.SuperAdminEmail);

        foreach (var route in new[] { "customer-view", "user-manage", "audit" })
        {
            using var response = await client.GetAsync($"/api/v1/_authz-probe/{route}");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    // ---- Baseline [Authorize]: autenticado sem permissão específica → 204 --

    [Fact]
    public async Task AuthenticatedOnlyEndpoint_AsClient_ReturnsNoContent()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(
            factory,
            AuthorizationWebApplicationFactory.ClientEmail);

        // Mesmo sem permissões, um usuário autenticado passa por [Authorize] simples.
        using var response = await client.GetAsync("/api/v1/_authz-probe/authenticated");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<HttpClient> AuthenticatedClientAsync(
        AuthorizationWebApplicationFactory factory,
        string email)
    {
        var client = factory.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = AuthorizationWebApplicationFactory.KnownPassword });
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    /// <summary>Espelho do <c>TokenResponse</c> da API para desserialização nos testes.</summary>
    private sealed record TokenResponseDto(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt);
}
