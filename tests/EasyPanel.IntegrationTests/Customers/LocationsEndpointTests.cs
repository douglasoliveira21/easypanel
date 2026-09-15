using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.IntegrationTests.Customers;

/// <summary>
/// Testes de integração dos endpoints de locais (Task 8.2 / R9.1, R9.4, R9.6,
/// R9.7, R9.8, R12.2, R12.4, R5.4). Exercitam o pipeline HTTP completo
/// (autenticação Bearer + policies <c>perm:location.*</c> + validação + serviço +
/// isolamento multi-tenant) sobre SQLite in-memory via
/// <see cref="LocationsWebApplicationFactory"/>.
/// </summary>
public sealed class LocationsEndpointTests
{
    private const string ValidCnpj = "11.222.333/0001-81";

    private static LocationsWebApplicationFactory CreateFactory() => new();

    // ---- Autorização: 401 e 403 (R5.4) ------------------------------------

    [Fact]
    public async Task Create_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId = Guid.NewGuid(), nome = "Matriz" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_AsTecnico_WithoutLocationCreate_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.TecnicoAEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId = Guid.NewGuid(), nome = "Matriz" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListByCustomer_AsCliente_WithoutLocationView_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.ClienteAEmail);

        using var response = await client.GetAsync($"/api/v1/customers/{Guid.NewGuid()}/locations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Create → 201, get por id, lista por cliente (R9.1/R9.6/R9.7) ------

    [Fact]
    public async Task Create_ThenGetAndListByCustomer_ReturnsLocation()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        var customerId = await CreateCustomerAsync(client, "Acme SA");

        using var create = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId, nome = "Matriz", responsavel = "João" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<LocationDto>();
        Assert.NotNull(created);
        Assert.Equal(customerId, created!.CustomerId);
        Assert.Equal("Matriz", created.Nome);
        Assert.Equal(0, created.Status); // Ativo

        // GET por id (R9.6).
        using var get = await client.GetAsync($"/api/v1/locations/{created.Id}");
        get.EnsureSuccessStatusCode();
        var fetched = await get.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Equal(created.Id, fetched!.Id);

        // Lista por cliente (R9.7).
        using var list = await client.GetAsync($"/api/v1/customers/{customerId}/locations?page=1&pageSize=100");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<LocationPageDto>();
        Assert.Contains(page!.Items, l => l.Id == created.Id);
    }

    // ---- Create: cliente inexistente → 400 (R9.4) -------------------------

    [Fact]
    public async Task Create_WithNonExistentCustomer_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId = Guid.NewGuid(), nome = "Órfão" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Create: cliente de outro tenant → 400 (R9.4) ---------------------

    [Fact]
    public async Task Create_WithCustomerOfAnotherTenant_ReturnsBadRequest()
    {
        using var factory = CreateFactory();

        // Cliente criado no Tenant B.
        using var clientB = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminBEmail);
        var customerB = await CreateCustomerAsync(clientB, "B Corp");

        // Tenant A tenta criar um local referenciando o cliente do Tenant B.
        using var clientA = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);
        using var response = await clientA.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId = customerB, nome = "Invasor" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Create: corpo inválido (sem nome) → 400 (R12.2) ------------------

    [Fact]
    public async Task Create_WithMissingNome_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        var customerId = await CreateCustomerAsync(client, "Acme SA");

        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId, nome = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Update → 200; inexistente → 404 (R9.5/R9.6) ----------------------

    [Fact]
    public async Task Update_ExistingLocation_ReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        var customerId = await CreateCustomerAsync(client, "Acme SA");
        var created = await CreateLocationAsync(client, customerId, "Original");

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/locations/{created.Id}",
            new { nome = "Alterado", responsavel = "Maria" });
        update.EnsureSuccessStatusCode();

        var updated = await update.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Equal("Alterado", updated!.Nome);
    }

    [Fact]
    public async Task Update_NonExistentLocation_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/locations/{Guid.NewGuid()}",
            new { nome = "Fantasma" });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    // ---- Get: local de outro tenant → 404 (R9.6) --------------------------

    [Fact]
    public async Task Get_LocationOfAnotherTenant_ReturnsNotFound()
    {
        using var factory = CreateFactory();

        using var clientA = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);
        var customerA = await CreateCustomerAsync(clientA, "A SA");
        var created = await CreateLocationAsync(clientA, customerA, "A Local");

        using var clientB = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminBEmail);
        using var get = await clientB.GetAsync($"/api/v1/locations/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---- PATCH status → 200 (R9.8) ----------------------------------------

    [Fact]
    public async Task ChangeStatus_PersistsNewStatus()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        var customerId = await CreateCustomerAsync(client, "Acme SA");
        var created = await CreateLocationAsync(client, customerId, "Matriz");

        using var patch = await client.PatchAsJsonAsync(
            $"/api/v1/locations/{created.Id}/status",
            new { status = 1 }); // Inativo
        patch.EnsureSuccessStatusCode();

        var updated = await patch.Content.ReadFromJsonAsync<LocationDto>();
        Assert.Equal(1, updated!.Status); // Inativo
    }

    // ---- Lista por cliente: paginação clamp ≤ 100 (R12.4) -----------------

    [Fact]
    public async Task ListByCustomer_WithOversizedPageSize_IsClampedTo100()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, LocationsWebApplicationFactory.AdminAEmail);

        var customerId = await CreateCustomerAsync(client, "Acme SA");

        using var list = await client.GetAsync($"/api/v1/customers/{customerId}/locations?page=1&pageSize=500");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<LocationPageDto>();

        Assert.Equal(100, page!.PageSize);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateCustomerAsync(HttpClient client, string razaoSocial)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial, cnpj = ValidCnpj });
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<CustomerIdDto>();
        Assert.NotNull(customer);
        return customer!.Id;
    }

    private static async Task<LocationDto> CreateLocationAsync(HttpClient client, Guid customerId, string nome)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId, nome });
        response.EnsureSuccessStatusCode();
        var location = await response.Content.ReadFromJsonAsync<LocationDto>();
        Assert.NotNull(location);
        return location!;
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(
        LocationsWebApplicationFactory factory,
        string email)
    {
        var client = factory.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = LocationsWebApplicationFactory.KnownPassword });
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

    private sealed record CustomerIdDto(Guid Id);

    private sealed record LocationDto(
        Guid Id,
        Guid TenantId,
        Guid CustomerId,
        string Nome,
        string? Endereco,
        string? Responsavel,
        int Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record LocationPageDto(
        IReadOnlyList<LocationDto> Items,
        int Page,
        int PageSize,
        long TotalCount);
}
