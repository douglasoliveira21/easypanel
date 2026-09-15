using System.Net;

namespace EasyPanel.IntegrationTests.Api;

/// <summary>
/// Testes de integração do rate limiting (Task 9.2 / R4.6, R4.7) através do
/// pipeline real. Com um limite baixo (<see cref="RateLimitingWebApplicationFactory.PermitLimit"/>
/// por janela), verifica que requisições acima do limite na mesma partição são
/// recusadas com HTTP 429 e que a resposta inclui <c>Retry-After</c>.
/// </summary>
public sealed class RateLimitingEndpointTests
    : IClassFixture<RateLimitingWebApplicationFactory>
{
    private readonly RateLimitingWebApplicationFactory _factory;

    public RateLimitingEndpointTests(RateLimitingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExceedingLimit_ReturnsTooManyRequests_WithRetryAfter()
    {
        using var client = _factory.CreateClient();

        // As primeiras PermitLimit requisições são permitidas...
        for (var i = 0; i < RateLimitingWebApplicationFactory.PermitLimit; i++)
        {
            using var ok = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // ...a próxima, na mesma partição/janela, é recusada com 429 (R4.7).
        using var limited = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }
}
