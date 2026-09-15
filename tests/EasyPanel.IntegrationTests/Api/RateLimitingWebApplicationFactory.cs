using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EasyPanel.IntegrationTests.Api;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dos testes de rate limiting
/// (Task 9.2 / R4.6, R4.7). Configura um limite propositalmente baixo
/// (<c>PermitLimit=2</c> por janela) para que poucas requisições disparem o 429,
/// e aponta as dependências obrigatórias para endereços inalcançáveis (o teste
/// usa o endpoint anônimo <c>/health/live</c>, que não toca dependências).
/// </summary>
public sealed class RateLimitingWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Limite de requisições por janela aplicado nos testes.</summary>
    public const int PermitLimit = 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Database:ConnectionString"] =
                    "Host=127.0.0.1;Port=1;Database=easypanel_test;Username=none;Password=none;Timeout=1;Command Timeout=1",
                ["Redis:ConnectionString"] = "127.0.0.1:1",
                ["Redis:TimeoutMilliseconds"] = "500",
                ["Storage:Endpoint"] = "http://127.0.0.1:1",
                ["Storage:TimeoutMilliseconds"] = "500",
                ["Jwt:Issuer"] = "easypanel-integration",
                ["Jwt:Audience"] = "easypanel-integration",
                ["Jwt:SigningKey"] = "integration-tests-signing-key-not-a-secret-32b+",

                // Limite baixo e janela longa: a 3ª requisição na mesma partição
                // dentro da janela é recusada com 429 (R4.7).
                ["RateLimiting:PermitLimit"] = PermitLimit.ToString(),
                ["RateLimiting:WindowSeconds"] = "300",
                ["RateLimiting:QueueLimit"] = "0",
            };

            configuration.AddInMemoryCollection(overrides);
        });
    }
}
