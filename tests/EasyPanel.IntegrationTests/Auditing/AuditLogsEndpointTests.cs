using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.IntegrationTests.Auditing;

/// <summary>
/// Testes de integração/segurança do endpoint <c>GET /api/v1/audit-logs</c>
/// (Task 5.2 / R10.5, R12.4, R5.4). Exercitam o pipeline HTTP completo
/// (autenticação Bearer + policy <c>perm:audit.view</c> + consulta restrita por
/// tenant) sobre SQLite in-memory:
/// <list type="bullet">
///   <item>sem autenticação → 401;</item>
///   <item>autenticado sem <c>audit.view</c> → 403 (R5.4);</item>
///   <item>com <c>audit.view</c> → 200 com página restrita ao tenant (R10.5);</item>
///   <item>PageSize é limitado a 100 (R12.4);</item>
///   <item>eventos de outro tenant não aparecem (R10.5).</item>
/// </list>
/// </summary>
public sealed class AuditLogsEndpointTests
{
    private static AuditLogsWebApplicationFactory CreateFactory() => new();

    [Fact]
    public async Task AuditLogs_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditLogs_AsOperational_WithoutPermission_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, AuditLogsWebApplicationFactory.OperationalAEmail);

        using var response = await client.GetAsync("/api/v1/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuditLogs_AsAdministrator_ReturnsTenantScopedPage()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, AuditLogsWebApplicationFactory.AdminAEmail);

        using var response = await client.GetAsync("/api/v1/audit-logs?page=1&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.NotNull(payload);

        // TotalCount reflete os eventos do Tenant A (R10.5): os semeados mais o
        // evento auth.login gerado pelo próprio login do administrador (ambos do
        // Tenant A). Nunca inclui eventos do Tenant B.
        Assert.True(
            payload!.TotalCount >= AuditLogsWebApplicationFactory.TenantAAuditRows,
            "A contagem deve incluir ao menos os eventos semeados do Tenant A.");
        Assert.Equal(25, payload.PageSize);
        Assert.Equal(25, payload.Items.Count);

        // Nenhum item pertence ao Tenant B (eventos "user.create"/"User" — R10.5).
        Assert.DoesNotContain(payload.Items, item => item.ResourceType == "User");
    }

    [Fact]
    public async Task AuditLogs_PageSizeAbove100_IsClampedTo100()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, AuditLogsWebApplicationFactory.AdminAEmail);

        using var response = await client.GetAsync("/api/v1/audit-logs?page=1&pageSize=500");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
        Assert.NotNull(payload);

        // PageSize limitado a 100 (R12.4); a página traz no máximo 100 itens.
        Assert.Equal(100, payload!.PageSize);
        Assert.Equal(100, payload.Items.Count);
    }

    [Fact]
    public async Task AuditLogs_DoesNotReturnOtherTenantRows()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, AuditLogsWebApplicationFactory.AdminAEmail);

        // Percorre todas as páginas e confirma que nenhum recurso do Tenant B
        // (ResourceType "User") jamais aparece (R10.5). Os eventos do Tenant A
        // incluem os "customer.update" semeados e o "auth.login" do próprio login.
        var seenSeeded = 0;
        for (var page = 1; page <= 2; page++)
        {
            using var response = await client.GetAsync($"/api/v1/audit-logs?page={page}&pageSize=100");
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<AuditLogPageDto>();
            Assert.NotNull(payload);

            // Isolamento por tenant: nenhum evento do Tenant B (R10.5).
            Assert.DoesNotContain(payload!.Items, item => item.ResourceType == "User");
            seenSeeded += payload.Items.Count(item =>
                item.ResourceId is not null && item.ResourceId.StartsWith("a-", StringComparison.Ordinal));
        }

        // Todos os eventos "customer.update" semeados do Tenant A são visíveis.
        Assert.Equal(AuditLogsWebApplicationFactory.TenantAAuditRows, seenSeeded);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<HttpClient> AuthenticatedClientAsync(
        AuditLogsWebApplicationFactory factory,
        string email)
    {
        var client = factory.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = AuditLogsWebApplicationFactory.KnownPassword });
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotNull(tokens);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private sealed record TokenResponseDto(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt);

    private sealed record AuditLogPageDto(
        IReadOnlyList<AuditLogItemDto> Items,
        int Page,
        int PageSize,
        long TotalCount);

    private sealed record AuditLogItemDto(
        Guid Id,
        Guid? ActorUserId,
        string Action,
        string ResourceType,
        string? ResourceId,
        int Result,
        DateTimeOffset OccurredAt,
        string? Ip,
        string? UserAgent,
        string? OldValues,
        string? NewValues);
}
