using System.Diagnostics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace EasyPanel.Api.Observability;

/// <summary>
/// Configuração de observabilidade da plataforma (R11): logs estruturados em JSON
/// (Serilog) e telemetria distribuída (OpenTelemetry).
///
/// <list type="bullet">
///   <item><b>Logs JSON (R11.1):</b> Serilog com <c>CompactJsonFormatter</c>
///     no console, adequado à coleta pelo Loki. As entradas são enriquecidas com o
///     <c>CorrelationId</c> e o <c>tenant_id</c> do escopo da requisição (R11.3),
///     empurrados pelos middlewares/serviços via <c>ILogger.BeginScope</c>.</item>
///   <item><b>Redação de segredos (R11.2):</b> a política de destructuring do
///     Serilog remove propriedades cujo nome sugira segredo (senha, token, segredo,
///     chave, hash, credencial) antes da serialização, evitando o vazamento de
///     dados sensíveis nos logs.</item>
///   <item><b>Traces e métricas (R11):</b> OpenTelemetry instrumenta ASP.NET Core e
///     HttpClient e exporta via OTLP para o collector (endpoint configurável). Sem
///     um collector acessível, a exportação apenas não entrega, sem afetar a app.</item>
/// </list>
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Nome do serviço reportado na telemetria.</summary>
    public const string ServiceName = "easypanel-api";

    /// <summary>
    /// Configura o Serilog como provedor de logs do host, com formato JSON compacto
    /// e redação de segredos (R11.1/R11.2/R11.3). Deve ser chamado no
    /// <see cref="IHostBuilder"/> antes de <c>Build()</c>.
    /// </summary>
    public static IHostBuilder UsePlatformSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, loggerConfiguration) =>
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Destructure.With(new SecretRedactionPolicy())
                .WriteTo.Console(new CompactJsonFormatter()));

    /// <summary>
    /// Registra o OpenTelemetry (traces + métricas) com instrumentação de ASP.NET
    /// Core e HttpClient e exportação OTLP para o collector (R11). O endpoint do
    /// collector é lido de <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (padrão do SDK).
    /// </summary>
    public static IServiceCollection AddPlatformOpenTelemetry(this IServiceCollection services)
    {
        var resource = ResourceBuilder.CreateDefault().AddService(ServiceName);

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter());

        return services;
    }
}
