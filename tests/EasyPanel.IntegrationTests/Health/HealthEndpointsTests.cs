using System.Net;

namespace EasyPanel.IntegrationTests.Health;

/// <summary>
/// Testes de integração dos endpoints de saúde (Task 1.5 / R1.3, R1.5).
///
/// Exercitam o pipeline HTTP real via <see cref="UnavailableDependenciesWebApplicationFactory"/>,
/// que aponta PostgreSQL, Redis e S3/MinIO para endereços inalcançáveis. Assim,
/// validamos que:
/// <list type="bullet">
///   <item><c>/health/ready</c> reporta estado não saudável (HTTP 503) quando
///   uma dependência obrigatória está indisponível (R1.5);</item>
///   <item><c>/health/live</c> permanece saudável (HTTP 200), pois não avalia
///   dependências (R1.3).</item>
/// </list>
/// </summary>
public sealed class HealthEndpointsTests
    : IClassFixture<UnavailableDependenciesWebApplicationFactory>
{
    private readonly UnavailableDependenciesWebApplicationFactory _factory;

    public HealthEndpointsTests(UnavailableDependenciesWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ready_WhenDependenciesUnavailable_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Live_IsHealthy_RegardlessOfDependencies()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
