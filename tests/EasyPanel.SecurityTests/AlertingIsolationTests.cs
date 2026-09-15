using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.SecurityTests;

/// <summary>
/// Suíte de segurança da Fase 3 (Alertas e Notificações) — Task 8.1 (R1.7, R3.8,
/// R5.6, R7.7, R8.2, R8.6): isolamento cross-tenant de <c>AlertRule</c>,
/// <c>Alert</c> e <c>AlertSilence</c> através da API HTTP real, ausência de
/// <see cref="AlertRule.WebhookSecret"/> em qualquer resposta de API, e negação
/// RBAC de <c>alert.manage</c> a um papel sem essa permissão.
/// </summary>
public sealed class AlertingIsolationTests : IClassFixture<MultiTenantIsolationWebApplicationFactory>
{
    private readonly MultiTenantIsolationWebApplicationFactory _factory;

    public AlertingIsolationTests(MultiTenantIsolationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AlertRule_OfAnotherTenant_IsNotReadableOrEditable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ruleId = await CreateRuleAsync(clientA, "Regra isolada de A");

        using var get = await clientB.GetAsync($"/api/v1/alert-rules/{ruleId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var put = await clientB.PutAsJsonAsync(
            $"/api/v1/alert-rules/{ruleId}",
            new
            {
                name = "Hijack",
                isActive = true,
                eventTypes = new[] { 2 },
                scopeType = 0,
                severity = 2,
                autoResolve = true,
                emailEnabled = false,
                webhookEnabled = false,
            });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task AlertRule_WebhookSecret_NeverAppearsInApiResponse()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);

        const string secret = "segredo-super-secreto-do-webhook";

        using var create = await clientA.PostAsJsonAsync(
            "/api/v1/alert-rules",
            new
            {
                name = "Regra com webhook",
                eventTypes = new[] { 2 },
                scopeType = 0,
                severity = 2,
                autoResolve = true,
                emailEnabled = false,
                webhookEnabled = true,
                webhookUrl = "https://example.com/hook",
                webhookSecret = secret,
            });
        create.EnsureSuccessStatusCode();

        var createBody = await create.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, createBody);

        var created = await create.Content.ReadFromJsonAsync<RuleIdRow>();

        using var get = await clientA.GetAsync($"/api/v1/alert-rules/{created!.Id}");
        get.EnsureSuccessStatusCode();
        var getBody = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, getBody);

        using var list = await clientA.GetAsync("/api/v1/alert-rules?page=1&pageSize=50");
        list.EnsureSuccessStatusCode();
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, listBody);
    }

    [Fact]
    public async Task Alert_OfAnotherTenant_IsNotReadableAcknowledgeableOrResolvable()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ruleId = await CreateRuleAsync(clientA, "Regra para alerta de A");
        var alertId = await SeedAlertDirectlyAsync(MultiTenantIsolationWebApplicationFactory.TenantA, ruleId);

        using var get = await clientB.GetAsync($"/api/v1/alerts/{alertId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var acknowledge = await clientB.PostAsync($"/api/v1/alerts/{alertId}/acknowledge", content: null);
        Assert.Equal(HttpStatusCode.NotFound, acknowledge.StatusCode);

        using var resolve = await clientB.PostAsJsonAsync($"/api/v1/alerts/{alertId}/resolve", new { note = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);

        using var notifications = await clientB.GetAsync($"/api/v1/alerts/{alertId}/notifications");
        Assert.Equal(HttpStatusCode.NotFound, notifications.StatusCode);
    }

    [Fact]
    public async Task AlertSilence_OfAnotherTenant_IsNotEndableEarly_AndDoesNotLeakInList()
    {
        using var clientA = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminAEmail);
        using var clientB = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.AdminBEmail);

        var ruleId = await CreateRuleAsync(clientA, "Regra para silêncio de A");

        using var create = await clientA.PostAsJsonAsync(
            "/api/v1/alert-silences",
            new
            {
                alertRuleId = ruleId,
                startsAt = DateTimeOffset.UtcNow,
                endsAt = DateTimeOffset.UtcNow.AddHours(1),
                reason = "Manutenção",
            });
        create.EnsureSuccessStatusCode();
        var silence = await create.Content.ReadFromJsonAsync<SilenceIdRow>();

        using var listB = await clientB.GetAsync("/api/v1/alert-silences?page=1&pageSize=50");
        listB.EnsureSuccessStatusCode();
        var page = await listB.Content.ReadFromJsonAsync<SilencePageDto>();
        Assert.DoesNotContain(page!.Items, s => s.Id == silence!.Id);

        using var end = await clientB.PostAsync($"/api/v1/alert-silences/{silence!.Id}/end", content: null);
        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithoutAlertManage_CannotCreateAlertRule()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var create = await tecnico.PostAsJsonAsync(
            "/api/v1/alert-rules",
            new
            {
                name = "Regra proibida",
                eventTypes = new[] { 2 },
                scopeType = 0,
                severity = 2,
                autoResolve = true,
                emailEnabled = false,
                webhookEnabled = false,
            });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Tecnico_WithAlertView_CanListAlertRules()
    {
        using var tecnico = await AuthedAsync(MultiTenantIsolationWebApplicationFactory.TecnicoAEmail);

        using var list = await tecnico.GetAsync("/api/v1/alert-rules?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<Guid> CreateRuleAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/alert-rules",
            new
            {
                name,
                eventTypes = new[] { 2 },
                scopeType = 0,
                severity = 2,
                autoResolve = true,
                emailEnabled = false,
                webhookEnabled = false,
            });
        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<RuleIdRow>();
        return row!.Id;
    }

    private async Task<Guid> SeedAlertDirectlyAsync(Guid tenantId, Guid ruleId)
    {
        using var scope = _factory.CreateDataScope();
        var factory = scope.ServiceProvider.GetRequiredService<ISystemDbContextFactory>();
        await using var context = factory.Create();

        var now = DateTimeOffset.UtcNow;
        var alertId = Guid.NewGuid();
        context.Set<Alert>().Add(new Alert
        {
            Id = alertId,
            TenantId = tenantId,
            AlertRuleId = ruleId,
            Severity = AlertSeverity.Critica,
            State = AlertState.Open,
            FirstOccurrenceAt = now,
            FirstOccurrenceAtTicks = now.UtcTicks,
            LastOccurrenceAt = now,
            LastOccurrenceAtTicks = now.UtcTicks,
            OccurrenceCount = 1,
            CreatedAt = now,
        });
        await context.SaveChangesAsync();
        return alertId;
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

    private sealed record RuleIdRow(Guid Id);

    private sealed record SilenceIdRow(Guid Id);

    private sealed record SilenceRow(Guid Id);

    private sealed record SilencePageDto(IReadOnlyList<SilenceRow> Items, int Page, int PageSize, long TotalCount);
}
