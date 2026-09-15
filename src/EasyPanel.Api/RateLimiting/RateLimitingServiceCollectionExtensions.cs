using System.Globalization;
using System.Threading.RateLimiting;
using EasyPanel.Modules.Tenancy;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyPanel.Api.RateLimiting;

/// <summary>
/// Configuração de DI do rate limiting (R4.6, R4.7) usando o middleware nativo do
/// .NET (<c>Microsoft.AspNetCore.RateLimiting</c>).
///
/// <para><b>Particionamento (R4.6).</b> Cada requisição é atribuída a uma partição
/// cuja chave combina o <b>escopo do chamador</b> — usuário autenticado (claim
/// <c>sub</c>), senão tenant (claim <c>tenant_id</c>), senão endereço IP — com o
/// <b>endpoint</b> (método + caminho). Assim o limite vale simultaneamente por
/// usuário, por tenant, por IP e por endpoint, nunca apenas por IP.</para>
///
/// <para><b>Recusa (R4.7).</b> Ao exceder o limite, o middleware responde HTTP 429
/// e, quando conhecido, inclui o cabeçalho <c>Retry-After</c> (em segundos).</para>
///
/// <para><b>Ponto de extensão — store distribuído.</b> Nesta fase o algoritmo usa
/// o armazenamento em processo do limiter nativo. Em uma implantação com múltiplas
/// instâncias, um contador distribuído (ex.: Redis) daria um limite global preciso;
/// isso é um ponto de extensão documentado (substituir por um
/// <see cref="RateLimiter"/> apoiado em Redis) sem alterar o particionamento nem o
/// pipeline. O contrato observável (429 + Retry-After por partição) permanece o
/// mesmo.</para>
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>Nome do claim de subject (Id do usuário) preservado no token.</summary>
    private const string SubClaimType = "sub";

    /// <summary>Registra e configura o rate limiter global particionado.</summary>
    public static IServiceCollection AddPlatformRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Inclui Retry-After quando a janela do limiter informa o tempo de
            // reabastecimento (R4.7).
            limiterOptions.OnRejected = (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                return ValueTask.CompletedTask;
            };

            limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var options = httpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>()
                    .Value;

                var partitionKey = BuildPartitionKey(httpContext);

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = options.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Constrói a chave de partição combinando o escopo do chamador (usuário →
    /// tenant → IP) com o endpoint (método + caminho), atendendo R4.6.
    /// </summary>
    private static string BuildPartitionKey(HttpContext httpContext)
    {
        var user = httpContext.User;

        string scope;
        if (user.Identity is { IsAuthenticated: true })
        {
            var userId = user.FindFirst(SubClaimType)?.Value;
            var tenantId = user.FindFirst(TenancyConstants.TenantIdClaimType)?.Value;

            scope = !string.IsNullOrWhiteSpace(userId)
                ? $"user:{userId}"
                : !string.IsNullOrWhiteSpace(tenantId)
                    ? $"tenant:{tenantId}"
                    : "auth:unknown";
        }
        else
        {
            var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            scope = $"ip:{ip}";
        }

        // O endpoint compõe a partição para que o limite seja por endpoint (R4.6).
        var endpoint = $"{httpContext.Request.Method}:{httpContext.Request.Path.Value}";

        return $"{scope}|{endpoint}";
    }
}
