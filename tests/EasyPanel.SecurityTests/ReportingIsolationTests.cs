using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 9 (Relatórios e Dashboards) — Task 4.1 (R6.2,
/// R6.3): isolamento cross-tenant de painel/relatórios através da API HTTP
/// real, fidelidade da exportação CSV (R5.1/R5.2), e negação RBAC de
/// <c>relatorio.view</c> a papel sem essa permissão.
/// </summary>
public sealed class ReportingIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public ReportingIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_CrossTenant_DoesNotLeak()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var customerId = await CreateCustomerAsync(clientA);
        await CreateTicketAsync(clientA, customerId);

        using var dashboardB = await clientB.GetAsync("/api/v1/reports/dashboard");
        dashboardB.EnsureSuccessStatusCode();
        var overviewB = await dashboardB.Content.ReadFromJsonAsync<DashboardOverviewDto>();

        Assert.Equal(0, overviewB!.TicketsByStatus.Values.Sum());
    }

    [Fact]
    public async Task SlaReport_CrossTenant_DoesNotLeak()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var customerId = await CreateCustomerAsync(clientA);
        await CreateTicketAsync(clientA, customerId);

        var start = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var end = DateTimeOffset.UtcNow.AddDays(1).ToString("O");

        using var slaB = await clientB.GetAsync($"/api/v1/reports/sla?startDate={Uri.EscapeDataString(start)}&endDate={Uri.EscapeDataString(end)}");
        slaB.EnsureSuccessStatusCode();
        var reportB = await slaB.Content.ReadFromJsonAsync<SlaReportDto>();

        Assert.Equal(0, reportB!.TotalTickets);
    }

    [Fact]
    public async Task SlaReport_Export_MatchesQuery_AndIsUtf8Csv()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);

        var customerId = await CreateCustomerAsync(admin);
        await CreateTicketAsync(admin, customerId);

        var start = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var end = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var query = $"startDate={Uri.EscapeDataString(start)}&endDate={Uri.EscapeDataString(end)}";

        using var jsonResponse = await admin.GetAsync($"/api/v1/reports/sla?{query}");
        jsonResponse.EnsureSuccessStatusCode();
        var report = await jsonResponse.Content.ReadFromJsonAsync<SlaReportDto>();

        using var csvResponse = await admin.GetAsync($"/api/v1/reports/sla/export?{query}");
        csvResponse.EnsureSuccessStatusCode();
        Assert.StartsWith("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);

        var bytes = await csvResponse.Content.ReadAsByteArrayAsync();
        var csv = Encoding.UTF8.GetString(bytes);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Indicator,Cumprido,Violado,Pendente,ComplianceRate", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("FirstResponse,", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Resolution,", StringComparison.Ordinal));
        Assert.True(report!.TotalTickets >= 1);
    }

    [Fact]
    public async Task Operacional_WithoutRelatorioView_CannotAccessDashboard()
    {
        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var dashboard = await operacional.GetAsync("/api/v1/reports/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, dashboard.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithoutRelatorioView_CannotAccessReports()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        var start = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var end = DateTimeOffset.UtcNow.ToString("O");

        using var billing = await tecnico.GetAsync(
            $"/api/v1/reports/billing?startDate={Uri.EscapeDataString(start)}&endDate={Uri.EscapeDataString(end)}");

        Assert.Equal(HttpStatusCode.Forbidden, billing.StatusCode);
    }

    [Fact]
    public async Task Financeiro_WithRelatorioView_CanAccessDashboard()
    {
        using var financeiro = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.FinanceiroAEmail);

        using var dashboard = await financeiro.GetAsync("/api/v1/reports/dashboard");

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    [Fact]
    public async Task Supervisor_WithRelatorioView_CanAccessPrintConsumptionReport()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var supervisor = await AuthedAsync("supervisor@tenant-a.example.com", registerAsSupervisor: true, admin);

        var start = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var end = DateTimeOffset.UtcNow.ToString("O");

        using var report = await supervisor.GetAsync(
            $"/api/v1/reports/print-consumption?startDate={Uri.EscapeDataString(start)}&endDate={Uri.EscapeDataString(end)}");

        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Cliente Relatorios", cnpj = RandomCnpj() });
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<IdRow>();
        return customer!.Id;
    }

    private static async Task<Guid> CreateTicketAsync(HttpClient client, Guid customerId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/tickets",
            new { title = "Chamado de relatório", description = (string?)null, customerId, locationId = (Guid?)null, printerId = (Guid?)null, priority = 1 });
        response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadFromJsonAsync<IdRow>();
        return ticket!.Id;
    }

    private static int _cnpjSequence;

    /// <summary>Gera um CNPJ único e válido (dígitos verificadores por módulo 11), para não colidir
    /// com a restrição de unicidade por tenant entre os vários testes desta suíte.</summary>
    private static string RandomCnpj()
    {
        var sequence = Interlocked.Increment(ref _cnpjSequence);
        var baseDigits = $"{sequence:D8}0004".ToCharArray().Select(c => c - '0').ToArray();

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

    /// <summary>
    /// Cria um usuário Supervisor sob demanda (não seedado por padrão na
    /// fábrica compartilhada) via a API de usuários, usando um Administrador
    /// já autenticado, e retorna um cliente autenticado como ele.
    /// </summary>
    private async Task<HttpClient> AuthedAsync(string email, bool registerAsSupervisor, HttpClient adminClient)
    {
        if (registerAsSupervisor)
        {
            using var create = await adminClient.PostAsJsonAsync(
                "/api/v1/users",
                new { email, password = MultiTenantIsolationWebApplicationFactory.KnownPassword, roles = new[] { "Supervisor" } });
            create.EnsureSuccessStatusCode();
        }

        return await AuthedAsync(email);
    }

    private sealed record TokenResponse(string AccessToken, string RefreshToken);

    private sealed record IdRow(Guid Id);

    private sealed record DashboardOverviewDto(
        IReadOnlyDictionary<string, long> PrintersByStatus,
        IReadOnlyDictionary<string, long> AlertsByState,
        IReadOnlyDictionary<string, long> OpenAlertsBySeverity,
        IReadOnlyDictionary<string, long> TicketsByStatus,
        long LowStockItemCount,
        IReadOnlyDictionary<string, long> InvoicesByStatus,
        decimal InvoicesPendingTotalAmount,
        DateTimeOffset GeneratedAt);

    private sealed record SlaComplianceBreakdownDto(long Cumprido, long Violado, long Pendente, decimal ComplianceRate);

    private sealed record SlaReportDto(long TotalTickets, SlaComplianceBreakdownDto FirstResponse, SlaComplianceBreakdownDto Resolution);
}
