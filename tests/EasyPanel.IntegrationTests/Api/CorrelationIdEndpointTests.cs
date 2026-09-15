using EasyPanel.Api.Middleware;
using EasyPanel.IntegrationTests.Health;

namespace EasyPanel.IntegrationTests.Api;

/// <summary>
/// Testes de integração HTTP do <see cref="CorrelationIdMiddleware"/> (Task 9.1 /
/// R11.3) através do pipeline real, usando o endpoint anônimo <c>/health/live</c>
/// via <see cref="UnavailableDependenciesWebApplicationFactory"/>. Verifica que a
/// resposta sempre carrega o cabeçalho <c>X-Correlation-Id</c> e que um valor
/// fornecido na requisição é preservado.
/// </summary>
public sealed class CorrelationIdEndpointTests
    : IClassFixture<UnavailableDependenciesWebApplicationFactory>
{
    private readonly UnavailableDependenciesWebApplicationFactory _factory;

    public CorrelationIdEndpointTests(UnavailableDependenciesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Response_AlwaysCarriesCorrelationIdHeader()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
        var value = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public async Task Response_EchoesProvidedCorrelationId()
    {
        using var client = _factory.CreateClient();

        const string provided = "test-correlation-id-9001";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, provided);

        using var response = await client.SendAsync(request);

        var value = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(provided, value);
    }
}
