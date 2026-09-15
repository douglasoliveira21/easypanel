using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 6 (Chamados/Helpdesk e SLA) — Task 5.1 (R6.2,
/// R6.3, R6.5): isolamento cross-tenant de chamados/interações/anexos/políticas
/// de SLA através da API HTTP real, e negação RBAC de <c>chamado.manage</c> e
/// <c>sla.manage</c> a papéis sem essas permissões.
/// </summary>
public sealed class TicketingIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public TicketingIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ticket_OfAnotherTenant_IsNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ticketId = await CreateTicketAsync(clientA);

        using var get = await clientB.GetAsync($"/api/v1/tickets/{ticketId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Ticket_OfAnotherTenant_DoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        await CreateTicketAsync(clientA);

        using var listB = await clientB.GetAsync("/api/v1/tickets?page=1&pageSize=50");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<TicketPageDto>();

        Assert.Empty(page!.Items);
    }

    [Fact]
    public async Task Interactions_OfAnotherTenantsTicket_AreNotReadable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ticketId = await CreateTicketAsync(clientA);

        using var interactions = await clientB.GetAsync($"/api/v1/tickets/{ticketId}/interactions");
        Assert.Equal(HttpStatusCode.NotFound, interactions.StatusCode);
    }

    [Fact]
    public async Task Assign_AgainstAnotherTenantsTicket_IsRejected()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ticketId = await CreateTicketAsync(clientA);

        using var assign = await clientB.PostAsJsonAsync(
            $"/api/v1/tickets/{ticketId}/assign", new { assignedToUserId = (Guid?)null });
        Assert.Equal(HttpStatusCode.NotFound, assign.StatusCode);
    }

    [Fact]
    public async Task SlaPolicy_OfAnotherTenant_DoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        using var set = await clientA.PostAsJsonAsync(
            "/api/v1/sla-policies", new { priority = 3, firstResponseMinutes = 15, resolutionMinutes = 60 });
        set.EnsureSuccessStatusCode();

        using var listB = await clientB.GetAsync("/api/v1/sla-policies");
        listB.EnsureSuccessStatusCode();
        var policiesB = await listB.Content.ReadFromJsonAsync<List<SlaPolicyRow>>();

        Assert.Empty(policiesB!);
    }

    [Fact]
    public async Task Attachment_CrossTenant_UploadThenDownload_IsIsolated()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ticketId = await CreateTicketAsync(clientA);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "foto.png");

        using var upload = await clientA.PostAsync($"/api/v1/tickets/{ticketId}/attachments", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentRow>();

        using var listB = await clientB.GetAsync($"/api/v1/tickets/{ticketId}/attachments");
        Assert.Equal(HttpStatusCode.NotFound, listB.StatusCode);

        using var downloadB = await clientB.GetAsync($"/api/v1/tickets/{ticketId}/attachments/{attachment!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, downloadB.StatusCode);

        using var downloadA = await clientA.GetAsync($"/api/v1/tickets/{ticketId}/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, downloadA.StatusCode);
    }

    [Fact]
    public async Task Operacional_WithoutChamadoManage_CannotCreateTicket()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);

        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var create = await operacional.PostAsJsonAsync(
            "/api/v1/tickets",
            new { title = "Chamado proibido", description = (string?)null, customerId, locationId = (Guid?)null, printerId = (Guid?)null, priority = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Operacional_WithChamadoView_CanListTickets()
    {
        using var operacional = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.OperacionalAEmail);

        using var list = await operacional.GetAsync("/api/v1/tickets?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithoutSlaManage_CannotSetSlaPolicy()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var set = await tecnico.PostAsJsonAsync(
            "/api/v1/sla-policies", new { priority = 0, firstResponseMinutes = 60, resolutionMinutes = 480 });

        Assert.Equal(HttpStatusCode.Forbidden, set.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithChamadoManage_CanCreateTicket()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);

        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var create = await tecnico.PostAsJsonAsync(
            "/api/v1/tickets",
            new { title = "Chamado do técnico", description = (string?)null, customerId, locationId = (Guid?)null, printerId = (Guid?)null, priority = 1 });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateTicketAsync(HttpClient client)
    {
        var customerId = await CreateCustomerAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/tickets",
            new { title = "Chamado de teste", description = (string?)null, customerId, locationId = (Guid?)null, printerId = (Guid?)null, priority = 1 });
        response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadFromJsonAsync<IdRow>();
        return ticket!.Id;
    }

    private static int _cnpjSequence;

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Cliente Chamados", cnpj = RandomCnpj() });
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<IdRow>();
        return customer!.Id;
    }

    /// <summary>Gera um CNPJ único e válido (dígitos verificadores por módulo 11), para não colidir
    /// com a restrição de unicidade por tenant entre os vários testes desta suíte.</summary>
    private static string RandomCnpj()
    {
        var sequence = Interlocked.Increment(ref _cnpjSequence);
        var baseDigits = $"{sequence:D8}0002".ToCharArray().Select(c => c - '0').ToArray();

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

    private sealed record TicketRow(Guid Id);

    private sealed record TicketPageDto(IReadOnlyList<TicketRow> Items, int Page, int PageSize, long TotalCount);

    private sealed record SlaPolicyRow(Guid Id, int Priority, int FirstResponseMinutes, int ResolutionMinutes);

    private sealed record AttachmentRow(Guid Id);
}
