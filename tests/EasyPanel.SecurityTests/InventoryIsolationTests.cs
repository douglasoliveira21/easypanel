using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 5 (Estoque) — Task 5.1 (R6.2, R6.3, R6.5):
/// isolamento cross-tenant de itens, movimentações, saldo e estoque mínimo
/// através da API HTTP real, e negação RBAC de <c>estoque.manage</c> a um
/// papel sem essa permissão.
/// </summary>
public sealed class InventoryIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public InventoryIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InventoryItem_OfAnotherTenant_IsNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var itemId = await CreateItemAsync(clientA, "Toner Preto");

        using var get = await clientB.GetAsync($"/api/v1/inventory-items/{itemId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var update = await clientB.PutAsJsonAsync(
            $"/api/v1/inventory-items/{itemId}",
            new { name = "Alterado", sku = (string?)null, unit = "unidade", supplyLabel = (string?)null, isActive = true, observations = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task InventoryItem_OfAnotherTenant_DoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        await CreateItemAsync(clientA, "Cilindro");

        using var listB = await clientB.GetAsync("/api/v1/inventory-items?page=1&pageSize=50");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<ItemPageDto>();

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Movement_AgainstAnotherTenantsItem_IsRejected()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var itemId = await CreateItemAsync(clientA, "Papel A4");
        var locationId = await CreateLocationAsync(clientB, "Local B");

        // O item pertence ao Tenant A; sob o contexto do Tenant B, o próprio item
        // não é encontrado (filtro global por tenant) — 404, antes de chegar à
        // validação de local/impressora.
        using var register = await clientB.PostAsJsonAsync(
            "/api/v1/inventory-movements",
            new { itemId, locationId, type = 0, adjustmentDirection = (int?)null, quantity = 10, printerId = (Guid?)null, reason = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, register.StatusCode);
    }

    [Fact]
    public async Task MovementHistory_OfAnotherTenantsItem_IsNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var itemId = await CreateItemAsync(clientA, "Fusor");

        using var history = await clientB.GetAsync($"/api/v1/inventory-items/{itemId}/movements");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
    }

    [Fact]
    public async Task Balance_OfAnotherTenantsLocation_IsNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var itemId = await CreateItemAsync(clientA, "Cartucho");
        var locationId = await CreateLocationAsync(clientA, "Local A");

        using var register = await clientA.PostAsJsonAsync(
            "/api/v1/inventory-movements",
            new { itemId, locationId, type = 0, adjustmentDirection = (int?)null, quantity = 5, printerId = (Guid?)null, reason = (string?)null });
        register.EnsureSuccessStatusCode();

        // O local pertence ao Tenant A; sob o contexto do Tenant B, o próprio
        // local não é encontrado (filtro global por tenant) — 404.
        using var balanceB = await clientB.GetAsync($"/api/v1/locations/{locationId}/inventory");
        Assert.Equal(HttpStatusCode.NotFound, balanceB.StatusCode);
    }

    [Fact]
    public async Task InventoryMinimum_OfAnotherTenant_DoesNotLeakInBelowList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var itemId = await CreateItemAsync(clientA, "Grampos");
        var locationId = await CreateLocationAsync(clientA, "Local Grampos");

        using var setMinimum = await clientA.PostAsJsonAsync(
            "/api/v1/inventory-minimums",
            new { itemId, locationId, minimumQuantity = 100 });
        setMinimum.EnsureSuccessStatusCode();

        using var belowB = await clientB.GetAsync("/api/v1/inventory-minimums/below?page=1&pageSize=50");
        belowB.EnsureSuccessStatusCode();
        var page = await belowB.Content.ReadFromJsonAsync<BelowMinimumPageDto>();

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Tecnico_WithoutEstoqueManage_CannotCreateItem()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var create = await tecnico.PostAsJsonAsync(
            "/api/v1/inventory-items",
            new { name = "Item Proibido", sku = (string?)null, unit = (string?)null, supplyLabel = (string?)null, observations = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithoutEstoqueManage_CannotRegisterMovement()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        var itemId = await CreateItemAsync(admin, "Item Tecnico");
        var locationId = await CreateLocationAsync(admin, "Local Tecnico");

        using var register = await tecnico.PostAsJsonAsync(
            "/api/v1/inventory-movements",
            new { itemId, locationId, type = 0, adjustmentDirection = (int?)null, quantity = 1, printerId = (Guid?)null, reason = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithEstoqueView_CanListItems()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var list = await tecnico.GetAsync("/api/v1/inventory-items?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateItemAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/inventory-items",
            new { name, sku = (string?)null, unit = (string?)null, supplyLabel = (string?)null, observations = (string?)null });
        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<IdRow>();
        return item!.Id;
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, string nome)
    {
        using var customerResponse = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Cliente Estoque", cnpj = RandomCnpj() });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<IdRow>();

        using var locationResponse = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId = customer!.Id, nome });
        locationResponse.EnsureSuccessStatusCode();
        var location = await locationResponse.Content.ReadFromJsonAsync<IdRow>();
        return location!.Id;
    }

    private static int _cnpjSequence;

    /// <summary>Gera um CNPJ único e válido (dígitos verificadores por módulo 11), para não colidir
    /// com a restrição de unicidade por tenant entre os vários testes desta suíte.</summary>
    private static string RandomCnpj()
    {
        var sequence = Interlocked.Increment(ref _cnpjSequence);
        var baseDigits = $"{sequence:D8}0001".ToCharArray().Select(c => c - '0').ToArray();

        var firstWeights = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var secondWeights = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var first = ComputeCheckDigit(baseDigits, firstWeights);
        var second = ComputeCheckDigit([.. baseDigits, first], secondWeights);

        return string.Concat(baseDigits) + first + second;
    }

    private static int ComputeCheckDigit(int[] digits, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            sum += digits[i] * weights[i];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
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

    private sealed record ItemRow(Guid Id);

    private sealed record ItemPageDto(IReadOnlyList<ItemRow> Items, int Page, int PageSize, long TotalCount);

    private sealed record BelowMinimumRow(Guid ItemId, Guid LocationId, int CurrentQuantity, int MinimumQuantity);

    private sealed record BelowMinimumPageDto(IReadOnlyList<BelowMinimumRow> Items, int Page, int PageSize, long TotalCount);
}
