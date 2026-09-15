using Microsoft.Extensions.Primitives;

namespace EasyPanel.Api.Middleware;

/// <summary>
/// Middleware que garante um identificador de correlação por requisição (R11.3).
///
/// <para>Lê o cabeçalho <see cref="HeaderName"/> da requisição; se ausente ou
/// vazio, gera um novo identificador. O valor é disponibilizado em
/// <see cref="HttpContext.TraceIdentifier"/> (usado como <c>traceId</c> nos
/// <c>ProblemDetails</c>), ecoado no cabeçalho de resposta e empurrado para um
/// escopo de log estruturado, de modo que todas as entradas de log da requisição
/// compartilhem o mesmo <c>CorrelationId</c> (R11.3).</para>
///
/// <para>Executa cedo no pipeline (antes do tratamento de erros), para que até
/// falhas não tratadas sejam correlacionadas.</para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Nome do cabeçalho HTTP de correlação.</summary>
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>Cria o middleware com o próximo delegate e o logger.</summary>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _logger = logger;
    }

    /// <summary>Resolve/propaga o identificador de correlação e prossegue no pipeline.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);

        // Torna o id o identificador de trace da requisição (aparece nos
        // ProblemDetails como traceId) e o ecoa na resposta.
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Escopo de log estruturado: todas as entradas da requisição carregam o
        // CorrelationId (R11.3).
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var provided)
            && !StringValues.IsNullOrEmpty(provided)
            && !string.IsNullOrWhiteSpace(provided.ToString()))
        {
            return provided.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }
}
