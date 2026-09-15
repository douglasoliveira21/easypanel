using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.IntegrationTests.Identity;

/// <summary>
/// Testes de integração dos endpoints <c>/api/v1/users</c> (Task 6.2 / R7.1,
/// R7.3, R7.4, R7.6, R7.7, R7.8, R12.1, R12.2, R5.4). Exercitam o pipeline HTTP
/// completo (autenticação Bearer + policy <c>perm:user.manage</c> + validação +
/// serviço + auditoria) sobre SQLite in-memory via
/// <see cref="UsersWebApplicationFactory"/>.
/// </summary>
public sealed class UsersEndpointTests
{
    private static UsersWebApplicationFactory CreateFactory() => new();

    // ---- Autorização: 401 e 403 (R5.4) ------------------------------------

    [Fact]
    public async Task Users_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_AsOperational_WithoutPermission_ReturnsForbidden()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.OperationalAEmail);

        using var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Create → 201 e aparece na listagem (R7.1, R7.7) ------------------

    [Fact]
    public async Task Create_ThenList_ReturnsCreatedUserInCallerTenant()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        const string email = "new.user@tenant-a.example.com";
        using var create = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = UsersWebApplicationFactory.KnownPassword, roles = new[] { "Operacional" } });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(created);
        Assert.Equal(email, created!.Email);
        Assert.Equal(UsersWebApplicationFactory.TenantA, created.TenantId);
        Assert.True(created.IsActive);
        Assert.Contains("Operacional", created.Roles);

        // O usuário criado aparece na listagem do tenant do chamador (R7.7).
        using var list = await client.GetAsync("/api/v1/users?page=1&pageSize=100");
        list.EnsureSuccessStatusCode();
        var page = await list.Content.ReadFromJsonAsync<UserPageDto>();
        Assert.NotNull(page);
        Assert.Contains(page!.Items, u => u.Email == email);
    }

    // ---- Create: email duplicado no mesmo tenant → 409 (R7.2) -------------

    [Fact]
    public async Task Create_WithDuplicateEmail_ReturnsConflict()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        const string email = "dup@tenant-a.example.com";
        using var first = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = UsersWebApplicationFactory.KnownPassword, roles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = UsersWebApplicationFactory.KnownPassword, roles = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---- Create: corpo inválido → 400 (R12.2) -----------------------------

    [Fact]
    public async Task Create_WithMissingEmailAndPassword_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "", password = "", roles = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithWeakPassword_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        // Email válido, senha fora da política (R3.7): validador de contrato passa
        // (só exige presença), mas o Identity rejeita → 400 (R12.2/R3.7).
        using var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "weak@tenant-a.example.com", password = "weak", roles = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Update: altera papéis → 200; inexistente/outro tenant → 404 (R7.3)

    [Fact]
    public async Task Update_ChangesRoles_ReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        var created = await CreateUserAsync(client, "update.me@tenant-a.example.com", new[] { "Operacional" });

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/users/{created.Id}",
            new { email = created.Email, mfaEnabled = true, roles = new[] { "Supervisor" } });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(updated);
        Assert.True(updated!.MfaEnabled);
        Assert.Equal(new[] { "Supervisor" }, updated.Roles);
    }

    [Fact]
    public async Task Update_NonExistentUser_ReturnsNotFound()
    {
        using var factory = CreateFactory();
        using var client = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        using var update = await client.PutAsJsonAsync(
            $"/api/v1/users/{Guid.NewGuid()}",
            new { email = "ghost@tenant-a.example.com", mfaEnabled = false, roles = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task Update_UserOfAnotherTenant_ReturnsNotFound()
    {
        using var factory = CreateFactory();

        // Cria um usuário no Tenant B (via administrador de B).
        using var clientB = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminBEmail);
        var victim = await CreateUserAsync(clientB, "victim@tenant-b.example.com", Array.Empty<string>());

        // Administrador do Tenant A tenta atualizar o usuário do Tenant B → 404 (R7.3).
        using var clientA = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);
        using var update = await clientA.PutAsJsonAsync(
            $"/api/v1/users/{victim.Id}",
            new { email = "hijack@tenant-a.example.com", mfaEnabled = false, roles = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    // ---- Deactivate → 204 e login recusado; Reactivate → 204 (R7.4, R7.6) -

    [Fact]
    public async Task Deactivate_ThenReactivate_ChangesLoginAbility()
    {
        using var factory = CreateFactory();
        using var admin = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        const string email = "toggle@tenant-a.example.com";
        var user = await CreateUserAsync(admin, email, Array.Empty<string>());

        // Antes da desativação o usuário consegue autenticar.
        using (var anon = factory.CreateClient())
        {
            using var loginOk = await anon.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = UsersWebApplicationFactory.KnownPassword });
            Assert.Equal(HttpStatusCode.OK, loginOk.StatusCode);
        }

        // Desativa (R7.4) → 204.
        using var deactivate = await admin.PostAsync($"/api/v1/users/{user.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        // Usuário inativo não consegue mais autenticar (R7.5).
        using (var anon = factory.CreateClient())
        {
            using var loginDenied = await anon.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = UsersWebApplicationFactory.KnownPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, loginDenied.StatusCode);
        }

        // Reativa (R7.6) → 204 e o login volta a funcionar.
        using var reactivate = await admin.PostAsync($"/api/v1/users/{user.Id}/reactivate", content: null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);

        using (var anon = factory.CreateClient())
        {
            using var loginAgain = await anon.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = UsersWebApplicationFactory.KnownPassword });
            Assert.Equal(HttpStatusCode.OK, loginAgain.StatusCode);
        }
    }

    // ---- Isolamento por tenant na listagem (R7.7) -------------------------

    [Fact]
    public async Task List_ReturnsOnlyCallerTenantUsers()
    {
        using var factory = CreateFactory();

        // Cria um usuário exclusivo do Tenant B.
        using var clientB = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminBEmail);
        await CreateUserAsync(clientB, "only-b@tenant-b.example.com", Array.Empty<string>());

        // A listagem do administrador do Tenant A não vê usuários do Tenant B (R7.7).
        using var clientA = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);
        using var list = await clientA.GetAsync("/api/v1/users?page=1&pageSize=100");
        list.EnsureSuccessStatusCode();

        var page = await list.Content.ReadFromJsonAsync<UserPageDto>();
        Assert.NotNull(page);
        Assert.All(page!.Items, u => Assert.Equal(UsersWebApplicationFactory.TenantA, u.TenantId));
        Assert.DoesNotContain(page.Items, u => u.Email == "only-b@tenant-b.example.com");
        Assert.DoesNotContain(page.Items, u => u.Email == UsersWebApplicationFactory.AdminBEmail);
    }

    // ---- Auditoria com ator enriquecido (R7.8) ----------------------------

    [Fact]
    public async Task Create_WritesAuditRow_AttributedToActingUser()
    {
        using var factory = CreateFactory();
        using var admin = await AuthenticatedClientAsync(factory, UsersWebApplicationFactory.AdminAEmail);

        var created = await CreateUserAsync(admin, "audited@tenant-a.example.com", Array.Empty<string>());

        // Verifica diretamente no banco que existe uma entrada de auditoria de
        // criação para o usuário, atribuída ao administrador atuante (R7.8).
        using var scope = factory.CreateDataScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var resourceId = created.Id.ToString();
        var auditRow = await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == UsersWebApplicationFactory.TenantA
                && a.Action == AuditActions.UserCreate
                && a.ResourceId == resourceId)
            .SingleOrDefaultAsync();

        Assert.NotNull(auditRow);
        Assert.Equal(factory.AdminAId, auditRow!.ActorUserId);
    }

    // ---- Helpers -----------------------------------------------------------

    private static async Task<UserDto> CreateUserAsync(HttpClient client, string email, string[] roles)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new { email, password = UsersWebApplicationFactory.KnownPassword, roles });
        response.EnsureSuccessStatusCode();

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.NotNull(user);
        return user!;
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(
        UsersWebApplicationFactory factory,
        string email)
    {
        var client = factory.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = UsersWebApplicationFactory.KnownPassword });
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

    private sealed record UserDto(
        Guid Id,
        string? Email,
        Guid? TenantId,
        bool IsActive,
        bool MfaEnabled,
        IReadOnlyList<string> Roles,
        DateTimeOffset CreatedAt);

    private sealed record UserPageDto(
        IReadOnlyList<UserDto> Items,
        int Page,
        int PageSize,
        long TotalCount);
}
