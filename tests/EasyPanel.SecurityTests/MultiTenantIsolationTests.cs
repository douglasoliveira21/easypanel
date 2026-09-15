using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte consolidada de isolamento multi-tenant através da API HTTP real (Task
/// 10.2 / R6.5, R6.8, R7.2, R8.5). Prova, de ponta a ponta, que um administrador
/// do Tenant A não consegue ler, editar ou alterar recursos do Tenant B em
/// Clientes, Locais e Usuários — recursos de outro tenant são indistinguíveis de
/// inexistentes (404, não-vazamento) — e que a unicidade (CNPJ/email) é por tenant.
/// </summary>
public sealed class MultiTenantIsolationTests
    : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    // A fixture (IClassFixture) compartilha uma única base SQLite entre os testes
    // desta classe; por isso cada teste usa CNPJs/emails próprios para evitar
    // colisão de unicidade por tenant entre execuções.
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public MultiTenantIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ---- Customer: leitura/edição/status cross-tenant → 404 (R6.5) --------

    [Fact]
    public async Task Customer_OfAnotherTenant_IsNotReadableEditableOrStatusChangeable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        const string cnpj = "45.723.174/0001-10";
        var customerId = await CreateCustomerAsync(clientA, "A-Only SA", cnpj);

        // B não lê o cliente de A.
        using var get = await clientB.GetAsync($"/api/v1/customers/{customerId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        // B não edita o cliente de A.
        using var put = await clientB.PutAsJsonAsync(
            $"/api/v1/customers/{customerId}",
            new { razaoSocial = "Hijack SA", cnpj });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        // B não altera o status do cliente de A.
        using var patch = await clientB.PatchAsJsonAsync(
            $"/api/v1/customers/{customerId}/status",
            new { status = 2 });
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task Customer_List_IsScopedToCallerTenant()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var idA = await CreateCustomerAsync(clientA, "Scoped A SA", "04.252.011/0001-10");

        using var listB = await clientB.GetAsync("/api/v1/customers?page=1&pageSize=100");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<PageDto<CustomerRow>>();

        Assert.DoesNotContain(page!.Items, c => c.Id == idA);
    }

    // ---- Customer: CNPJ único por tenant, permitido entre tenants (R8.5) --

    [Fact]
    public async Task Customer_Cnpj_IsUniquePerTenant_ButAllowedAcrossTenants()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        const string cnpj = "11.222.333/0001-81";

        // Mesmo CNPJ em A e B: permitido (unicidade é por tenant).
        var first = await clientA.PostAsJsonAsync("/api/v1/customers", new { razaoSocial = "Dup A", cnpj });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var inB = await clientB.PostAsJsonAsync("/api/v1/customers", new { razaoSocial = "Dup B", cnpj });
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);

        // Repetir dentro do mesmo tenant A: conflito (409).
        var dupInA = await clientA.PostAsJsonAsync("/api/v1/customers", new { razaoSocial = "Dup A2", cnpj });
        Assert.Equal(HttpStatusCode.Conflict, dupInA.StatusCode);
    }

    // ---- Location: leitura/edição cross-tenant → 404 (R6.5) ---------------

    [Fact]
    public async Task Location_OfAnotherTenant_IsNotReadableOrEditable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var customerId = await CreateCustomerAsync(clientA, "Loc Owner SA", "34.028.316/0001-03");
        var locationId = await CreateLocationAsync(clientA, customerId, "Matriz A");

        using var get = await clientB.GetAsync($"/api/v1/locations/{locationId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var put = await clientB.PutAsJsonAsync(
            $"/api/v1/locations/{locationId}",
            new { nome = "Hijack" });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    // ---- User: edição/desativação cross-tenant → 404; email por tenant ----

    [Fact]
    public async Task User_OfAnotherTenant_IsNotEditableOrDeactivatable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        // A cria um usuário no seu tenant.
        var userId = await CreateUserAsync(clientA, "member@tenant-a.example.com");

        // B não enxerga esse usuário na sua listagem.
        using var listB = await clientB.GetAsync("/api/v1/users?page=1&pageSize=100");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<PageDto<UserRow>>();
        Assert.DoesNotContain(page!.Items, u => u.Id == userId);

        // B não edita nem desativa o usuário de A.
        using var put = await clientB.PutAsJsonAsync(
            $"/api/v1/users/{userId}",
            new { email = "hijack@tenant-b.example.com", mfaEnabled = false, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        using var deactivate = await clientB.PostAsync($"/api/v1/users/{userId}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);
    }

    [Fact]
    public async Task User_Email_IsUniquePerTenant_ButAllowedAcrossTenants()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        const string email = "shared@example.com";

        // Mesmo email em A e B: permitido (unicidade por tenant — R7.2).
        var inA = await clientA.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = MultiTenantIsolationWebApplicationFactory.KnownPassword, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Created, inA.StatusCode);

        var inB = await clientB.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = MultiTenantIsolationWebApplicationFactory.KnownPassword, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);

        // Repetir no mesmo tenant A: conflito (409).
        var dupInA = await clientA.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = MultiTenantIsolationWebApplicationFactory.KnownPassword, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, dupInA.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateCustomerAsync(HttpClient client, string razaoSocial, string cnpj)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/customers", new { razaoSocial, cnpj });
        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<IdRow>();
        return row!.Id;
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, Guid customerId, string nome)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/locations", new { customerId, nome });
        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<IdRow>();
        return row!.Id;
    }

    private static async Task<Guid> CreateUserAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = MultiTenantIsolationWebApplicationFactory.KnownPassword, roles = Array.Empty<string>() });
        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<IdRow>();
        return row!.Id;
    }

    private async Task<HttpClient> AuthedAsync(string email)
    {
        var client = _factory.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = MultiTenantIsolationWebApplicationFactory.KnownPassword });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken);

    private sealed record IdRow(Guid Id);

    private sealed record CustomerRow(Guid Id);

    private sealed record UserRow(Guid Id);

    private sealed record PageDto<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, long TotalCount);
}
