using System.Net;
using System.Net.Http.Json;
using EasyPanel.Infrastructure.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EasyPanel.IntegrationTests.Auth;

/// <summary>
/// Testes de integração dos endpoints de autenticação (Task 3.3 / R2.1, R2.2,
/// R2.3, R2.5, R4.3, R4.4, R4.5, R7.5). Exercitam o pipeline HTTP completo
/// (autenticação Bearer, Identity, emissão/rotação de tokens) sobre SQLite
/// in-memory via <see cref="AuthWebApplicationFactory"/>.
///
/// Cada teste cria a própria factory (e, portanto, seu próprio banco/usuários
/// semeados) para isolar o estado de lockout entre cenários.
/// </summary>
public sealed class AuthEndpointsTests
{
    private static AuthWebApplicationFactory CreateFactory() => new();

    // ---- Login sucesso (R2.1) ---------------------------------------------

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokensWithExpectedClaims()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        // O JWT carrega o tenant_id do usuário e um subject.
        var jwt = new JsonWebToken(tokens.AccessToken);
        Assert.Equal(
            AuthWebApplicationFactory.TenantA.ToString(),
            jwt.GetClaim(TokenService.TenantIdClaimType).Value);
        Assert.False(string.IsNullOrWhiteSpace(jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value));
    }

    // ---- Login falha genérica (R2.2) --------------------------------------

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsGenericUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            "Wr0ng!Password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsSameShapeAsWrongPassword()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var unknown = await PostLoginAsync(client, "nobody@example.com", "Wr0ng!Password");
        using var wrongPassword = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            "Wr0ng!Password");

        // Não-vazamento (R2.2): mesmo status para email desconhecido e senha errada.
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
    }

    // ---- Login usuário inativo (R7.5) -------------------------------------

    [Fact]
    public async Task Login_WithInactiveUser_IsRefused()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.InactiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Lockout após 5 falhas (R4.3, R4.4) -------------------------------

    [Fact]
    public async Task Login_AfterFiveConsecutiveFailures_LocksAccount()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // 5 falhas consecutivas disparam o bloqueio (R4.3).
        for (var i = 0; i < 5; i++)
        {
            using var fail = await PostLoginAsync(
                client,
                AuthWebApplicationFactory.ActiveUserEmail,
                "Wr0ng!Password");
            Assert.Equal(HttpStatusCode.Unauthorized, fail.StatusCode);
        }

        // Mesmo com a senha correta, a conta permanece bloqueada (R4.4).
        using var correct = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    // ---- Sucesso zera o contador (R4.5) -----------------------------------

    [Fact]
    public async Task Login_SuccessAfterFewFailures_ResetsCounter()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // 4 falhas (abaixo do limite de 5).
        for (var i = 0; i < 4; i++)
        {
            using var fail = await PostLoginAsync(
                client,
                AuthWebApplicationFactory.ActiveUserEmail,
                "Wr0ng!Password");
            Assert.Equal(HttpStatusCode.Unauthorized, fail.StatusCode);
        }

        // Login válido deve funcionar e zerar o contador (R4.5).
        using var success = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        // Com o contador zerado, 4 novas falhas ainda não bloqueiam.
        for (var i = 0; i < 4; i++)
        {
            using var fail = await PostLoginAsync(
                client,
                AuthWebApplicationFactory.ActiveUserEmail,
                "Wr0ng!Password");
            Assert.Equal(HttpStatusCode.Unauthorized, fail.StatusCode);
        }

        using var stillWorks = await PostLoginAsync(
            client,
            AuthWebApplicationFactory.ActiveUserEmail,
            AuthWebApplicationFactory.KnownPassword);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    // ---- Refresh válido rotaciona (R2.4, R2.5) ----------------------------

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokensAndInvalidatesOld()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var tokens = await LoginAndGetTokensAsync(client);

        using var refreshResponse = await PostRefreshAsync(client, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var rotated = await refreshResponse.Content.ReadFromJsonAsync<TokenResponseDto>();
        Assert.NotNull(rotated);
        Assert.NotEqual(tokens.RefreshToken, rotated!.RefreshToken);

        // O refresh token antigo não pode mais ser usado (rotação/revogação).
        using var reuse = await PostRefreshAsync(client, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await PostRefreshAsync(client, "not-a-real-refresh-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Logout revoga o refresh token (R2.3) -----------------------------

    [Fact]
    public async Task Logout_ThenRefreshWithSameToken_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var tokens = await LoginAndGetTokensAsync(client);

        using var logout = await client.PostAsJsonAsync(
            "/api/v1/auth/logout",
            new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Após o logout, o refresh token da sessão não é mais aceito (R2.3).
        using var refresh = await PostRefreshAsync(client, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    // ---- Health permanece anônimo após wiring de autenticação -------------

    [Fact]
    public async Task Live_RemainsAnonymous_AfterAuthenticationWiring()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Helpers -----------------------------------------------------------

    private static Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password) =>
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
