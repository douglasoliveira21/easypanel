using System.Net;
using System.Net.Http.Json;
using EasyPanel.IntegrationTests.Auth;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.IntegrationTests.Auditing;

/// <summary>
/// Testes de integração da auditoria automática de eventos de login (Task 5.2 /
/// R4.1, R10.2). Exercitam o endpoint de login de ponta a ponta (sobre SQLite
/// in-memory via <see cref="AuthWebApplicationFactory"/>) e verificam que cada
/// tentativa — sucesso ou falha — grava um registro <c>auth.login</c> na trilha,
/// com o resultado correto, o IP capturado e o ator atribuído quando conhecido.
/// A senha jamais aparece na trilha (R11.2).
/// </summary>
public sealed class LoginAuditTests
{
    private static AuthWebApplicationFactory CreateFactory() => new();

    [Fact]
    public async Task Login_Successful_WritesSuccessAuditRowWithActorAndIp()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = AuthWebApplicationFactory.ActiveUserEmail, password = AuthWebApplicationFactory.KnownPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logs = GetAuditLogs(factory);
        var login = Assert.Single(logs, a => a.Action == AuditActions.AuthLogin);

        Assert.Equal(AuditResult.Success, login.Result);
        Assert.Equal(AuditActions.AuthResourceType, login.ResourceType);
        Assert.NotNull(login.ActorUserId);
        Assert.Equal(AuthWebApplicationFactory.TenantA, login.TenantId);

        // A senha nunca é persistida na trilha (R11.2).
        Assert.DoesNotContain(AuthWebApplicationFactory.KnownPassword, Serialize(login), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WrongPasswordOnKnownUser_WritesFailureAuditRowAttributedToActor()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var expectedActorId = await GetUserIdAsync(factory, AuthWebApplicationFactory.ActiveUserEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = AuthWebApplicationFactory.ActiveUserEmail, password = "Wr0ng!Password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var logs = GetAuditLogs(factory);
        var login = Assert.Single(logs, a => a.Action == AuditActions.AuthLogin);

        Assert.Equal(AuditResult.Failure, login.Result);
        // Falha em conta conhecida é atribuída ao usuário (R4.1).
        Assert.Equal(expectedActorId, login.ActorUserId);
        Assert.Equal(AuthWebApplicationFactory.TenantA, login.TenantId);
    }

    [Fact]
    public async Task Login_UnknownEmail_WritesFailureAuditRowWithoutActor()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = "nobody@example.com", password = "Wr0ng!Password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var logs = GetAuditLogs(factory);
        var login = Assert.Single(logs, a => a.Action == AuditActions.AuthLogin);

        Assert.Equal(AuditResult.Failure, login.Result);
        // Email desconhecido: sem ator/tenant atribuível (R10.2).
        Assert.Null(login.ActorUserId);
        Assert.Null(login.TenantId);
    }

    // ---- Helpers -----------------------------------------------------------

    private static List<AuditLog> GetAuditLogs(AuthWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return context.AuditLogs.AsNoTracking().ToList();
    }

    private static async Task<Guid> GetUserIdAsync(AuthWebApplicationFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user!.Id;
    }

    private static string Serialize(AuditLog log) =>
        $"{log.Action}|{log.ResourceType}|{log.ResourceId}|{log.Ip}|{log.OldValues}|{log.NewValues}";
}
