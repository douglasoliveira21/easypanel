using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EasyPanel.IntegrationTests.Health;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> que sobe a API real
/// apontando as dependências obrigatórias (PostgreSQL, Redis e S3/MinIO) para
/// endereços inalcançáveis, simulando o cenário de indisponibilidade de R1.5.
///
/// A aplicação de migrações na inicialização é desabilitada
/// (<c>Database:ApplyMigrationsOnStartup=false</c>) para que o host suba sem
/// depender de um banco real — o objetivo do teste é validar o comportamento do
/// endpoint <c>/health/ready</c>, e não a migração.
/// </summary>
public sealed class UnavailableDependenciesWebApplicationFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Endereços deliberadamente inalcançáveis. A porta 1 (TCP) e o host
            // de documentação (reservado, sem serviço) garantem falha rápida de
            // conexão sem depender do ambiente de execução.
            var overrides = new Dictionary<string, string?>
            {
                // Não tentar migrar na inicialização (sem banco real disponível).
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Database:ConnectionString"] =
                    "Host=127.0.0.1;Port=1;Database=easypanel_test;Username=none;Password=none;Timeout=1;Command Timeout=1",

                // Redis inalcançável, timeout curto para o teste ser rápido.
                ["Redis:ConnectionString"] = "127.0.0.1:1",
                ["Redis:TimeoutMilliseconds"] = "500",

                // Storage S3/MinIO inalcançável.
                ["Storage:Endpoint"] = "http://127.0.0.1:1",
                ["Storage:TimeoutMilliseconds"] = "500",

                // Chave de assinatura de JWT presente para satisfazer a validação
                // das JwtOptions na inicialização (ValidateOnStart). Valor de
                // teste; não é usado nesta suíte de health checks.
                ["Jwt:SigningKey"] = "integration-tests-signing-key-not-a-secret-32b+",
            };

            configuration.AddInMemoryCollection(overrides);
        });
    }
}
