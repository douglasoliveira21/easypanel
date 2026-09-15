using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EasyPanel.IntegrationTests.Auth;

/// <summary>
/// Testes de integração dos fluxos de recuperação e alteração de senha (Task 3.5 /
/// R3.1–R3.7). Exercitam o pipeline HTTP completo sobre SQLite in-memory via
/// <see cref="AuthWebApplicationFactory"/>, usando o
/// <see cref="CapturingPasswordResetNotifier"/> para recuperar o token de
/// redefinição emitido fora de banda.
///
/// Cobre:
/// <list type="bullet">
///   <item>forgot-password para email cadastrado → 204 e token emitido (R3.1).</item>
///   <item>forgot-password para email desconhecido → mesma resposta, sem token (R3.2).</item>
///   <item>reset-password com token válido → nova senha funciona, antiga falha (R3.3).</item>
///   <item>reset/change revogam sessões ativas (R3.4).</item>
///   <item>reutilização do token de reset → falha (uso único — R3.3).</item>
///   <item>change-password autenticado com senha atual correta → 204 (R3.5).</item>
///   <item>change-password com senha atual incorreta → 400 (R3.6).</item>
///   <item>reset/change com senha fraca → 400 (R3.7).</item>
/// </list>
/// </summary>
public sealed class PasswordEndpointsTests
{
    private static AuthWebApplicationFactory CreateFactory() => new();

    // ---- forgot-password: email cadastrado emite token (R3.1) --------------

    [Fact]
    public async Task ForgotPassword_ForRegisteredEmail_ReturnsNoContentAndIssuesToken()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostForgotAsync(client, AuthWebApplicationFactory.ActiveUserEmail);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(factory.ResetNotifier.TokenFor(AuthWebApplicationFactory.ActiveUserEmail));
        Assert.Equal(1, factory.ResetNotifier.SentCount);
    }

    // ---- forgot-password: email desconhecido não vaza existência (R3.2) ----

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_ReturnsSameShapeAndIssuesNoToken()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var known = await PostForgotAsync(client, AuthWebApplicationFactory.ActiveUserEmail);
        using var unknown = await PostForgotAsync(client, "nobody@example.com");

        // Mesma resposta para email cadastrado e não cadastrado (R3.2).
        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);

        // Nenhum token foi emitido para o email desconhecido.
        Assert.Null(factory.ResetNotifier.TokenFor("nobody@example.com"));
    }

    // ---- forgot-password: conta inativa não emite token, mesma resposta ----

    [Fact]
    public async Task ForgotPassword_ForInactiveUser_ReturnsSameShapeAndIssuesNoToken()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostForgotAsync(client, AuthWebApplicationFactory.InactiveUserEmail);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(factory.ResetNotifier.TokenFor(AuthWebApplicationFactory.InactiveUserEmail));
    }

    // ---- reset-password: token válido troca a senha (R3.3) -----------------

    [Fact]
    public async Task ResetPassword_WithValidToken_ChangesPasswordSoNewWorksAndOldFails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        const string newPassword = "N3w!Passw0rd";

        await PostForgotAsync(client, AuthWebApplicationFactory.ActiveUserEmail);
        var token = factory.ResetNotifier.TokenFor(AuthWebApplicationFactory.ActiveUserEmail)!;

        using var reset = await PostResetAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            token,
            newPassword);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // A nova senha autentica; a antiga não (R3.3).
        using var withNew = await PostLoginAsync(client, AuthWebApplicationFactory.ActiveUserEmail, newPassword);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        using var withOld = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    // ---- reset-password: revoga sessões ativas (R3.4) ----------------------

    [Fact]
    public async Task ResetPassword_RevokesExistingRefreshTokens()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        const string newPassword = "N3w!Passw0rd";

        // Sessão ativa antes do reset.
        var tokens = await LoginAndGetTokensAsync(client);

        await PostForgotAsync(client, AuthWebApplicationFactory.ActiveUserEmail);
        var token = factory.ResetNotifier.TokenFor(AuthWebApplicationFactory.ActiveUserEmail)!;

        using var reset = await PostResetAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            token,
            newPassword);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // O refresh token antigo não é mais válido após a troca de senha (R3.4).
        using var refresh = await PostRefreshAsync(client, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // ---- reset-password: uso único (R3.3) ----------------------------------

    [Fact]
    public async Task ResetPassword_ReusingSameToken_FailsSecondTime()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        await PostForgotAsync(client, AuthWebApplicationFactory.ActiveUserEmail);
        var token = factory.ResetNotifier.TokenFor(AuthWebApplicationFactory.ActiveUserEmail)!;

        using var first = await PostResetAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            token,
            "N3w!Passw0rd");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // O mesmo token não pode ser reutilizado: o SecurityStamp mudou (R3.3).
        using var second = await PostResetAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            token,
            "An0ther!Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    // ---- reset-password: senha fraca é recusada (R3.7) ---------------------

    [Fact]
    public async Task ResetPassword_WithWeakPassword_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        await PostForgotAsync(client, AuthWebApplicationFactory.ActiveUserEmail);
        var token = factory.ResetNotifier.TokenFor(AuthWebApplicationFactory.ActiveUserEmail)!;

        using var reset = await PostResetAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            token,
            "weak");
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    // ---- reset-password: token inválido é recusado (R3.3) ------------------

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var reset = await PostResetAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            "not-a-valid-token",
            "N3w!Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    // ---- change-password: autenticado com senha correta (R3.5) -------------

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_ChangesPasswordAndRevokesSessions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        const string newPassword = "N3w!Passw0rd";

        var tokens = await LoginAndGetTokensAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = AuthWebApplicationFactory.KnownPassword,
                newPassword,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var change = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // Nova senha funciona; antiga falha (R3.5).
        using var withNew = await PostLoginAsync(client, AuthWebApplicationFactory.ActiveUserEmail, newPassword);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        using var withOld = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);

        // Sessão anterior revogada (R3.4).
        using var refresh = await PostRefreshAsync(client, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // ---- change-password: senha atual incorreta (R3.6) ---------------------

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsBadRequestAndKeepsPassword()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var tokens = await LoginAndGetTokensAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = "Wr0ng!Password",
                newPassword = "N3w!Passw0rd",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var change = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);

        // A senha original permanece válida (R3.6).
        using var stillWorks = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    // ---- change-password: sem autenticação → 401 --------------------------

    [Fact]
    public async Task ChangePassword_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var change = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { currentPassword = AuthWebApplicationFactory.KnownPassword, newPassword = "N3w!Passw0rd" });

        Assert.Equal(HttpStatusCode.Unauthorized, change.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static Task<HttpResponseMessage> PostForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });

    private static Task<HttpResponseMessage> PostResetAsync(
        HttpClient client,
        string email,
        string token,
        string newPassword) =>
        client.PostAsJsonAsync("/api/v1/auth/reset-password", new { email, token, newPassword });

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private static Task<HttpResponseMessage> PostRefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    private static async Task<TokenResponseDto> LoginAndGetTokensAsync(HttpClient client)
    {
        using var response = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotNull(tokens);
        return tokens!;
    }

    /// <summary>Espelho do <c>TokenResponse</c> da API para desserialização nos testes.</summary>
    private sealed record TokenResponseDto(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset AccessTokenExpiresAt,
        DateTimeOffset RefreshTokenExpiresAt);
}
