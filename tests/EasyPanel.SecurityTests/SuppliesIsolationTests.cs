using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 4 (Suprimentos) — Task 9.1 (R6.2, R6.3, R6.5):
/// isolamento cross-tenant de níveis/histórico de suprimento e de limiares de
/// suprimento através da API HTTP real, e negação RBAC de <c>supply.manage</c> a
/// um papel sem essa permissão.
/// </summary>
public sealed class SuppliesIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public SuppliesIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Supplies_OfAnotherTenantsPrinter_AreNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var printerId = await CreatePrinterAsync(clientA, "45.723.174/0001-10");

        using var levels = await clientB.GetAsync($"/api/v1/printers/{printerId}/supplies");
        Assert.Equal(HttpStatusCode.NotFound, levels.StatusCode);

        using var history = await clientB.GetAsync($"/api/v1/printers/{printerId}/supplies/history");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
    }

    [Fact]
    public async Task SupplyThreshold_OfAnotherTenant_DoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        using var create = await clientA.PostAsJsonAsync(
            "/api/v1/supply-thresholds",
            new { printerId = (Guid?)null, label = (string?)null, thresholdPercent = 12 });
        create.EnsureSuccessStatusCode();

        using var listB = await clientB.GetAsync("/api/v1/supply-thresholds?page=1&pageSize=50");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<ThresholdPageDto>();

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Tecnico_WithoutSupplyManage_CannotSetThreshold()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var create = await tecnico.PostAsJsonAsync(
            "/api/v1/supply-thresholds",
            new { printerId = (Guid?)null, label = (string?)null, thresholdPercent = 10 });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithSupplyView_CanListThresholds()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var list = await tecnico.GetAsync("/api/v1/supply-thresholds?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreatePrinterAsync(HttpClient client, string cnpj)
    {
        using var customerResponse = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Cliente Suprimentos", cnpj });
        customerResponse.EnsureSuccessStatusCode();
        var customer = await customerResponse.Content.ReadFromJsonAsync<IdRow>();

        using var locationResponse = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId = customer!.Id, nome = "Local Suprimentos" });
        locationResponse.EnsureSuccessStatusCode();
        var location = await locationResponse.Content.ReadFromJsonAsync<IdRow>();

        using var printerResponse = await client.PostAsJsonAsync(
            "/api/v1/printers",
            new { customerId = customer.Id, locationId = location!.Id, ip = "10.9.9.9" });
        printerResponse.EnsureSuccessStatusCode();
        var printer = await printerResponse.Content.ReadFromJsonAsync<IdRow>();
        return printer!.Id;
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

    private sealed record ThresholdRow(Guid Id);

    private sealed record ThresholdPageDto(IReadOnlyList<ThresholdRow> Items, int Page, int PageSize, long TotalCount);
}
