using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Monitoring;

using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 10 (Portal do Cliente) — Task 6.1 (R2.4, R6.3):
/// um Usuário-Cliente nunca acessa parque/chamados/faturas de outro Cliente do
/// mesmo tenant nem de outro tenant; RBAC nega <c>portal.*.view</c> a papéis
/// administrativos/operacionais e nega permissões de negócio a um
/// Usuário-Cliente; caminho positivo; Fatura em Rascunho nunca aparece.
/// </summary>
public sealed class PortalIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public PortalIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cliente_CanListAndGetOwnFleetTicketsAndInvoices()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);
        var ticketId = await CreateTicketAsync(admin, customerId);
        var (printerId, _) = SeedPrinterWithCounter(MultiTenantIsolationWebApplicationFactory.TenantA, customerId);
        var invoiceId = SeedInvoice(
            MultiTenantIsolationWebApplicationFactory.TenantA, customerId, printerId, InvoiceStatus.Emitida);

        using var cliente = await CreateAndAuthAsClienteAsync(admin, "cliente1@tenant-a.example.com", customerId);

        using var printers = await cliente.GetAsync("/api/v1/portal/printers");
        Assert.Equal(HttpStatusCode.OK, printers.StatusCode);
        var printersPage = await printers.Content.ReadFromJsonAsync<CursorPage<PrinterRow>>();
        Assert.Contains(printersPage!.Items, p => p.Id == printerId);

        using var counters = await cliente.GetAsync($"/api/v1/portal/printers/{printerId}/counters");
        Assert.Equal(HttpStatusCode.OK, counters.StatusCode);

        using var tickets = await cliente.GetAsync("/api/v1/portal/tickets");
        Assert.Equal(HttpStatusCode.OK, tickets.StatusCode);
        var ticketsPage = await tickets.Content.ReadFromJsonAsync<CursorPage<IdRow>>();
        Assert.Contains(ticketsPage!.Items, t => t.Id == ticketId);

        using var ticketDetail = await cliente.GetAsync($"/api/v1/portal/tickets/{ticketId}");
        Assert.Equal(HttpStatusCode.OK, ticketDetail.StatusCode);

        using var invoices = await cliente.GetAsync("/api/v1/portal/invoices");
        Assert.Equal(HttpStatusCode.OK, invoices.StatusCode);
        var invoicesPage = await invoices.Content.ReadFromJsonAsync<CursorPage<IdRow>>();
        Assert.Contains(invoicesPage!.Items, i => i.Id == invoiceId);

        using var invoiceDetail = await cliente.GetAsync($"/api/v1/portal/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, invoiceDetail.StatusCode);
    }

    [Fact]
    public async Task Cliente_CannotAccessAnotherCustomerOfSameTenant()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerOwn = await CreateCustomerAsync(admin);
        var customerOther = await CreateCustomerAsync(admin);

        var otherTicketId = await CreateTicketAsync(admin, customerOther);
        var (otherPrinterId, _) = SeedPrinterWithCounter(MultiTenantIsolationWebApplicationFactory.TenantA, customerOther);
        var otherInvoiceId = SeedInvoice(
            MultiTenantIsolationWebApplicationFactory.TenantA, customerOther, otherPrinterId, InvoiceStatus.Emitida);

        using var cliente = await CreateAndAuthAsClienteAsync(admin, "cliente2@tenant-a.example.com", customerOwn);

        using var counters = await cliente.GetAsync($"/api/v1/portal/printers/{otherPrinterId}/counters");
        Assert.Equal(HttpStatusCode.NotFound, counters.StatusCode);

        using var ticketDetail = await cliente.GetAsync($"/api/v1/portal/tickets/{otherTicketId}");
        Assert.Equal(HttpStatusCode.NotFound, ticketDetail.StatusCode);

        using var invoiceDetail = await cliente.GetAsync($"/api/v1/portal/invoices/{otherInvoiceId}");
        Assert.Equal(HttpStatusCode.NotFound, invoiceDetail.StatusCode);

        using var printers = await cliente.GetAsync("/api/v1/portal/printers");
        var printersPage = await printers.Content.ReadFromJsonAsync<CursorPage<PrinterRow>>();
        Assert.DoesNotContain(printersPage!.Items, p => p.Id == otherPrinterId);
    }

    [Fact]
    public async Task Cliente_CannotAccessAnotherTenant()
    {
        using var adminA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var adminB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var customerA = await CreateCustomerAsync(adminA);
        var customerB = await CreateCustomerAsync(adminB);

        var (printerB, _) = SeedPrinterWithCounter(MultiTenantIsolationWebApplicationFactory.TenantB, customerB);

        using var clienteA = await CreateAndAuthAsClienteAsync(adminA, "cliente3@tenant-a.example.com", customerA);

        using var counters = await clienteA.GetAsync($"/api/v1/portal/printers/{printerB}/counters");
        Assert.Equal(HttpStatusCode.NotFound, counters.StatusCode);
    }

    [Fact]
    public async Task DraftInvoice_NeverAppears_ForOwnCustomer()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);
        var (printerId, _) = SeedPrinterWithCounter(MultiTenantIsolationWebApplicationFactory.TenantA, customerId);
        var draftInvoiceId = SeedInvoice(
            MultiTenantIsolationWebApplicationFactory.TenantA, customerId, printerId, InvoiceStatus.Rascunho);

        using var cliente = await CreateAndAuthAsClienteAsync(admin, "cliente4@tenant-a.example.com", customerId);

        using var invoices = await cliente.GetAsync("/api/v1/portal/invoices");
        var invoicesPage = await invoices.Content.ReadFromJsonAsync<CursorPage<IdRow>>();
        Assert.DoesNotContain(invoicesPage!.Items, i => i.Id == draftInvoiceId);

        using var invoiceDetail = await cliente.GetAsync($"/api/v1/portal/invoices/{draftInvoiceId}");
        Assert.Equal(HttpStatusCode.NotFound, invoiceDetail.StatusCode);
    }

    [Fact]
    public async Task Administrador_WithoutPortalPermissions_CannotAccessPortal()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);

        using var printers = await admin.GetAsync("/api/v1/portal/printers");

        Assert.Equal(HttpStatusCode.Forbidden, printers.StatusCode);
    }

    [Fact]
    public async Task Cliente_CannotAccessBusinessEndpointsOutsideThePortal()
    {
        using var admin = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        var customerId = await CreateCustomerAsync(admin);

        using var cliente = await CreateAndAuthAsClienteAsync(admin, "cliente5@tenant-a.example.com", customerId);

        using var tickets = await cliente.GetAsync("/api/v1/tickets");
        Assert.Equal(HttpStatusCode.Forbidden, tickets.StatusCode);

        using var customers = await cliente.GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Forbidden, customers.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateCustomerAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/customers",
            new { razaoSocial = "Cliente Portal", cnpj = RandomCnpj() });
        response.EnsureSuccessStatusCode();
        var customer = await response.Content.ReadFromJsonAsync<IdRow>();
        return customer!.Id;
    }

    private static async Task<Guid> CreateTicketAsync(HttpClient client, Guid customerId)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/tickets",
            new { title = "Chamado do portal", description = (string?)null, customerId, locationId = (Guid?)null, printerId = (Guid?)null, priority = 1 });
        response.EnsureSuccessStatusCode();
        var ticket = await response.Content.ReadFromJsonAsync<IdRow>();
        return ticket!.Id;
    }

    private (Guid PrinterId, Guid LocationId) SeedPrinterWithCounter(Guid tenantId, Guid customerId)
    {
        using var scope = _factory.CreateDataScope();
        var factory = scope.ServiceProvider.GetRequiredService<ISystemDbContextFactory>();
        using var context = factory.Create();

        var now = DateTimeOffset.UtcNow;
        var locationId = Guid.NewGuid();
        context.Set<Location>().Add(new Location
        {
            Id = locationId, TenantId = tenantId, CustomerId = customerId, Nome = "Local do Portal", CreatedAt = now,
        });

        var printerId = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = printerId, TenantId = tenantId, CustomerId = customerId, LocationId = locationId,
            Status = PrinterStatus.Online, MonitoringEnabled = true, CreatedAt = now,
        });

        context.Set<PrinterCounter>().Add(new PrinterCounter
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PrinterId = printerId,
            Timestamp = now,
            TimestampTicks = now.UtcTicks,
            CounterType = CounterType.BlackAndWhite,
            Value = 500,
            Source = CounterSource.Automatic,
            CreatedAt = now,
        });

        context.SaveChanges();
        return (printerId, locationId);
    }

    private Guid SeedInvoice(Guid tenantId, Guid customerId, Guid printerId, InvoiceStatus status)
    {
        using var scope = _factory.CreateDataScope();
        var factory = scope.ServiceProvider.GetRequiredService<ISystemDbContextFactory>();
        using var context = factory.Create();

        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

        var contractId = Guid.NewGuid();
        context.Set<Contract>().Add(new Contract
        {
            Id = contractId, TenantId = tenantId, Number = $"C-{contractId:N}"[..10], CustomerId = customerId,
            StartDate = periodStart, StartDateTicks = periodStart.UtcTicks,
            Status = ContractStatus.Ativo, CreatedAt = now, CreatedAtTicks = now.UtcTicks,
        });

        var invoiceId = Guid.NewGuid();
        context.Set<Invoice>().Add(new Invoice
        {
            Id = invoiceId, TenantId = tenantId, ContractId = contractId, CustomerId = customerId,
            PeriodStart = periodStart, PeriodStartTicks = periodStart.UtcTicks,
            PeriodEnd = periodEnd, PeriodEndTicks = periodEnd.UtcTicks,
            Status = status, TotalAmount = 15.00m, GeneratedAt = now, CreatedAt = now,
        });

        context.Set<InvoiceLineItem>().Add(new InvoiceLineItem
        {
            Id = Guid.NewGuid(), TenantId = tenantId, InvoiceId = invoiceId, PrinterId = printerId,
            CounterType = BillingCounterType.BlackAndWhite, ConsumedQuantity = 650, IncludedQuantity = 500,
            ExcessQuantity = 150, UnitPrice = 0.10m, LineAmount = 15.00m, CreatedAt = now,
        });

        context.SaveChanges();
        return invoiceId;
    }

    /// <summary>
    /// Cria um usuário Cliente sob demanda, vinculado ao Cliente informado, via a
    /// API de usuários (Administrador já autenticado), e retorna um cliente HTTP
    /// autenticado como ele.
    /// </summary>
    private async Task<HttpClient> CreateAndAuthAsClienteAsync(HttpClient adminClient, string email, Guid customerId)
    {
        using var create = await adminClient.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email,
                password = MultiTenantIsolationWebApplicationFactory.KnownPassword,
                roles = new[] { "Cliente" },
                customerId,
            });
        create.EnsureSuccessStatusCode();

        return await AuthedAsync(email);
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

    private sealed record TokenResponse(string AccessToken, string RefreshToken);

    private sealed record IdRow(Guid Id);

    private sealed record PrinterRow(Guid Id);

    private sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
}
