using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.IntegrationTests.Customers;

/// <summary>
/// Testes de integração dos endpoints <c>/api/v1/customers</c> (Task 7.2 / R8.1,
/// R8.4, R8.5, R8.7, R8.8, R8.9, R12.2, R12.4, R5.4). Exercitam o pipeline HTTP
/// completo (autenticação Bearer + policies <c>perm:customer.*</c> + validação +
/// serviço + isolamento multi-tenant) sobre SQLite in-memory via
/// <see cref="CustomersWebApplicationFactory"/>.
/// </summary>
public sealed class CustomersEndpointTests
{
    // CNPJ válido (dígitos verificadores corretos), enviado formatado para exercitar
    // a normalização (R8.4). Normaliza para "11222333000181".
    private const string ValidCnpjFormatted = "11.222.333/0001-81";
    private const string ValidCnpjNormalized = "11222333000181";
    private const string AnotherValidCnpj = "04.252.011/0001-10"; // normaliza para 04252011000110
    private const string InvalidCnpj = "11.222.333/0001-99"; // dígitos verificadores errados

    private static CustomersWebApplicationFactory CreateFactory() => new();

    // ---- Autorização: 401 e 403 (R5.4) ------------------------------------

    [Fact]
    public async Task Customers_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_AsCliente_WithoutCustomerView_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.ClienteAEmail);

        using var response = await client.GetAsync("/api/v1/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsTecnico_WithoutCustomerCreate_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.TecnicoAEmail);

        using var response = await client.PostAsJsonAsync("/api/v1/customers", ValidCreateBody("Acme SA"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Create → 201, aparece na lista, get por id (R8.1, R8.7, R8.8) ----

    [Fact]
    public async Task Create_ThenGetAndList_ReturnsCustomerInCallerTenant()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        using var create = await client.PostAsJsonAsync("/api/v1/customers", ValidCreateBody("Acme SA"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(created);
        Assert.Equal("Acme SA", created!.RazaoSocial);
        Assert.Equal(ValidCnpjNormalized, created.Cnpj); // normalizado (R8.4)
        Assert.Equal(CustomersWebApplicationFactory.TenantA, created.TenantId);
        Assert.Equal(0, created.Status); // Ativo

        // GET por id (R8.7).
        using var get = await client.GetAsync($"/api/v1/customers/{created.Id}");
        get.EnsureSuccessStatusCode();
        var fetched = await get.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.Equal(created.Id, fetched!.Id);

        // Aparece na listagem do tenant (R8.8).
        using var list = await client.GetAsync("/api/v1/customers?page=1&pageSize=100");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<CustomerPageDto>();
        Assert.Contains(page!.Items, c => c.Id == created.Id);
    }

    // ---- Create: CNPJ inválido → 400 (R8.4) -------------------------------

    [Fact]
    public async Task Create_WithInvalidCnpj_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Bad Cnpj SA", cnpj = InvalidCnpj });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Create: corpo inválido (sem razão social) → 400 (R12.2) ----------

    [Fact]
    public async Task Create_WithMissingRazaoSocial_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "", cnpj = ValidCnpjFormatted });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Create: CNPJ duplicado no mesmo tenant → 409 (R8.5) --------------

    [Fact]
    public async Task Create_WithDuplicateCnpjInSameTenant_ReturnsConflict()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        using var first = await client.PostAsJsonAsync("/api/v1/customers", ValidCreateBody("Primeira SA"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/v1/customers", ValidCreateBody("Segunda SA"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---- Mesmo CNPJ em tenant diferente é permitido (unicidade por tenant) --

    [Fact]
    public async Task Create_SameCnpjInDifferentTenant_IsAllowed()
    {
        using var factory = CreateFactory();

        using var clientA = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);
        using var createA = await clientA.PostAsJsonAsync("/api/v1/customers", ValidCreateBody("Tenant A Corp"));
        Assert.Equal(HttpStatusCode.Created, createA.StatusCode);

        using var clientB = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminBEmail);
        using var createB = await clientB.PostAsJsonAsync("/api/v1/customers", ValidCreateBody("Tenant B Corp"));
        Assert.Equal(HttpStatusCode.Created, createB.StatusCode);
    }

    // ---- Update → 200; update de outro tenant/inexistente → 404 (R8.6/R8.7) --

    [Fact]
    public async Task Update_ExistingCustomer_ReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        var created = await CreateCustomerAsync(client, "Original SA");

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/customers/{created.Id}",
            new { razaoSocial = "Alterada SA", cnpj = ValidCnpjFormatted });
        update.EnsureSuccessStatusCode();

        var updated = await update.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.Equal("Alterada SA", updated!.RazaoSocial);
    }

    [Fact]
    public async Task Update_NonExistentCustomer_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/customers/{Guid.NewGuid()}",
            new { razaoSocial = "Fantasma SA", cnpj = ValidCnpjFormatted });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task Get_CustomerOfAnotherTenant_ReturnsNotFound()
    {
        using var factory = CreateFactory();

        using var clientA = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);
        var created = await CreateCustomerAsync(clientA, "A-only SA");

        // Tenant B não enxerga o cliente do Tenant A (R8.7).
        using var clientB = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminBEmail);
        using var get = await clientB.GetAsync($"/api/v1/customers/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---- List isolada por tenant (R8.8) -----------------------------------

    [Fact]
    public async Task List_ReturnsOnlyCallerTenantCustomers()
    {
        using var factory = CreateFactory();

        using var clientA = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);
        var a = await CreateCustomerAsync(clientA, "Somente A SA");

        using var clientB = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminBEmail);
        using var listB = await clientB.GetAsync("/api/v1/customers?page=1&pageSize=100");
        listB.EnsureSuccessStatusCode();
        var pageB = await listB.Content.ReadFromJsonAsync<CustomerPageDto>();

        Assert.DoesNotContain(pageB!.Items, c => c.Id == a.Id);
    }

    // ---- PATCH status → 200 (R8.9) ----------------------------------------

    [Fact]
    public async Task ChangeStatus_PersistsNewStatus()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        var created = await CreateCustomerAsync(client, "Status SA");

        using var patch = await client.PatchAsJsonAsync(
            $"/api/v1/customers/{created.Id}/status",
            new { status = 2 }); // Bloqueado
        patch.EnsureSuccessStatusCode();

        var updated = await patch.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.Equal(2, updated!.Status); // Bloqueado
    }

    // ---- Paginação: PageSize limitado a ≤ 100 (R12.4) ---------------------

    [Fact]
    public async Task List_WithOversizedPageSize_IsClampedTo100()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, CustomersWebApplicationFactory.AdminAEmail);

        using var list = await client.GetAsync("/api/v1/customers?page=1&pageSize=500");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<CustomerPageDto>();

        Assert.Equal(100, page!.PageSize);
    }

    // ---- Helpers -----------------------------------------------------------

    private static object ValidCreateBody(string razaoSocial) => new
    {
        razaoSocial,
        cnpj = ValidCnpjFormatted,
        nomeFantasia = (string?)null,
    };

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string razaoSocial)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/customers", ValidCreateBody(razaoSocial));
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(customer);
        return customer!;
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(
        CustomersWebApplicationFactory factory,
        string email)
    {
        var client = factory.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = CustomersWebApplicationFactory.KnownPassword });
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

    private sealed record CustomerDto(
        Guid Id,
        Guid TenantId,
        string RazaoSocial,
        string? NomeFantasia,
        string Cnpj,
        int Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record CustomerPageDto(
        IReadOnlyList<CustomerDto> Items,
        int Page,
        int PageSize,
        long TotalCount);
}
