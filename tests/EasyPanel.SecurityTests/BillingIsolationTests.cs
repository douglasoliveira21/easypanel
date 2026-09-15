using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 8 (Fechamento e Faturamento) — Task 5.1 (R7.2,
/// R7.3, R7.5): isolamento cross-tenant de fechamentos/faturas através da
/// API HTTP real, e negação RBAC de <c>fechamento.view</c>/
/// <c>fechamento.manage</c> a papel sem essas permissões.
/// </summary>
public sealed class BillingIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public BillingIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BillingClosingHistory_OfAnotherTenant_DoesNotLeak()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        // Período distante e sem nenhuma impressora do Tenant A: fecha com sucesso, 0 faturas.
        using var close = await clientA.PostAsJsonAsync("/api/v1/billing-closings", new { year = 2020, month = 1 });
        Assert.Equal(HttpStatusCode.Created, close.StatusCode);

        using var listB = await clientB.GetAsync("/api/v1/billing-closings?page=1&pageSize=10");
        listB.EnsureSuccessStatusCode();
        var pageB = await listB.Content.ReadFromJsonAsync<BillingClosingPageDto>();

        Assert.Empty(pageB!.Items);
    }

    [Fact]
    public async Task Invoice_OfAnotherTenant_DoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        using var close = await clientA.PostAsJsonAsync("/api/v1/billing-closings", new { year = 2020, month = 2 });
        close.EnsureSuccessStatusCode();

        using var listB = await clientB.GetAsync("/api/v1/invoices?page=1&pageSize=10");
        listB.EnsureSuccessStatusCode();
        var pageB = await listB.Content.ReadFromJsonAsync<InvoicePageDto>();

        Assert.Empty(pageB!.Items);
    }

    [Fact]
    public async Task Closing_FuturePeriod_Rejected()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);

        using var close = await admin.PostAsJsonAsync(
            "/api/v1/billing-closings", new { year = DateTimeOffset.UtcNow.Year + 1, month = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, close.StatusCode);
    }

    [Fact]
    public async Task Operacional_WithoutFechamentoManage_CannotExecuteClosing()
    {
        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var close = await operacional.PostAsJsonAsync("/api/v1/billing-closings", new { year = 2020, month = 3 });

        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
    }

    [Fact]
    public async Task Operacional_WithoutFechamentoView_CannotListInvoices()
    {
        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var list = await operacional.GetAsync("/api/v1/invoices?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithoutFechamentoPermission_CannotListBillingClosings()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var list = await tecnico.GetAsync("/api/v1/billing-closings?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Financeiro_WithFechamentoManage_CanExecuteClosing()
    {
        using var financeiro = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.FinanceiroAEmail);

        using var close = await financeiro.PostAsJsonAsync("/api/v1/billing-closings", new { year = 2020, month = 4 });

        Assert.Equal(HttpStatusCode.Created, close.StatusCode);
    }

    [Fact]
    public async Task Financeiro_WithFechamentoView_CanListInvoices()
    {
        using var financeiro = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.FinanceiroAEmail);

        using var list = await financeiro.GetAsync("/api/v1/invoices?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

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

    private sealed record BillingClosingRow(Guid Id, int Year, int Month);

    private sealed record BillingClosingPageDto(IReadOnlyList<BillingClosingRow> Items, int Page, int PageSize, long TotalCount);

    private sealed record InvoiceRow(Guid Id);

    private sealed record InvoicePageDto(IReadOnlyList<InvoiceRow> Items, int Page, int PageSize, long TotalCount);
}
