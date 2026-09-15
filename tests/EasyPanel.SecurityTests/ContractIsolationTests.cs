using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 7 (Contratos) — Task 5.1 (R6.2, R6.3, R6.5):
/// isolamento cross-tenant de contratos/escopo/franquias através da API HTTP
/// real, e negação RBAC de <c>contrato.manage</c> a papel sem essa permissão.
/// </summary>
public sealed class ContractIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public ContractIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Contract_OfAnotherTenant_IsNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var contractId = await CreateContractAsync(clientA);

        using var get = await clientB.GetAsync($"/api/v1/contracts/{contractId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Contract_OfAnotherTenant_DoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        await CreateContractAsync(clientA);

        using var listB = await clientB.GetAsync("/api/v1/contracts?page=1&pageSize=50");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<ContractPageDto>();

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Scope_AgainstAnotherTenantsContract_IsRejected()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var contractId = await CreateContractAsync(clientA);
        var locationId = await CreateLocationAsync(clientB);

        using var addLocation = await clientB.PostAsync($"/api/v1/contracts/{contractId}/locations/{locationId}", null);
        Assert.Equal(HttpStatusCode.NotFound, addLocation.StatusCode);
    }

    [Fact]
    public async Task Franchise_OfAnotherTenantsContract_NotFound()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var contractId = await CreateContractAsync(clientA);

        using var list = await clientB.GetAsync($"/api/v1/contracts/{contractId}/franchises");
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task ApplicableContract_ForAnotherTenantsPrinter_ReturnsNoContent()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var printerId = await CreatePrinterAsync(clientA);

        using var resolve = await clientB.GetAsync($"/api/v1/printers/{printerId}/applicable-contract");
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);
    }

    [Fact]
    public async Task Operacional_WithoutContratoManage_CannotCreateContract()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);

        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var create = await operacional.PostAsJsonAsync(
            "/api/v1/contracts",
            new { number = "C-Proibido", customerId, startDate = DateTimeOffset.UtcNow, endDate = (DateTimeOffset?)null, observations = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Operacional_WithContratoView_CanListContracts()
    {
        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var list = await operacional.GetAsync("/api/v1/contracts?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithoutAnyContratoPermission_CannotListContracts()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var list = await tecnico.GetAsync("/api/v1/contracts?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Financeiro_WithContratoManage_CanCreateContract()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);

        using var financeiro = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.FinanceiroAEmail);

        using var create = await financeiro.PostAsJsonAsync(
            "/api/v1/contracts",
            new { number = "C-Financeiro", customerId, startDate = DateTimeOffset.UtcNow, endDate = (DateTimeOffset?)null, observations = (string?)null });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateContractAsync(HttpClient client)
    {
        var customerId = await CreateCustomerAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/contracts",
            new { number = "C-Teste", customerId, startDate = DateTimeOffset.UtcNow, endDate = (DateTimeOffset?)null, observations = (string?)null });
        response.EnsureSuccessStatusCode();
        var contract = await response.Content.ReadFromJsonAsync<IdRow>();
        return contract!.Id;
    }

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Cliente Contratos", cnpj = RandomCnpj() });
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<IdRow>();
        return customer!.Id;
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client)
    {
        var customerId = await CreateCustomerAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId, nome = "Local Contratos" });
        response.EnsureSuccessStatusCode();
        var location = await response.Content.ReadFromJsonAsync<IdRow>();
        return location!.Id;
    }

    private static async Task<Guid> CreatePrinterAsync(HttpClient client)
    {
        var customerId = await CreateCustomerAsync(client);

        using var locationResponse = await client.PostAsJsonAsync(
            "/api/v1/locations",
            new { customerId, nome = "Local Impressora" });
        locationResponse.EnsureSuccessStatusCode();
        var location = await locationResponse.Content.ReadFromJsonAsync<IdRow>();

        using var printerResponse = await client.PostAsJsonAsync(
            "/api/v1/printers",
            new { customerId, locationId = location!.Id, ip = "10.9.8.7" });
        printerResponse.EnsureSuccessStatusCode();
        var printer = await printerResponse.Content.ReadFromJsonAsync<IdRow>();
        return printer!.Id;
    }

    private static int _cnpjSequence;

    /// <summary>Gera um CNPJ único e válido (dígitos verificadores por módulo 11), para não colidir
    /// com a restrição de unicidade por tenant entre os vários testes desta suíte.</summary>
    private static string RandomCnpj()
    {
        var sequence = Interlocked.Increment(ref _cnpjSequence);
        var baseDigits = $"{sequence:D8}0003".ToCharArray().Select(c => c - '0').ToArray();

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

    private sealed record ContractRow(Guid Id);

    private sealed record ContractPageDto(IReadOnlyList<ContractRow> Items, int Page, int PageSize, long TotalCount);
}
