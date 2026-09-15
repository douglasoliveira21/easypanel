using EasyPanel.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.IntegrationTests.Api;

/// <summary>
/// Unit tests do <see cref="CorrelationIdMiddleware"/> (Task 9.1 / R11.3):
/// resolução/geração do identificador de correlação, sua propagação para o
/// <see cref="HttpContext.TraceIdentifier"/> e o eco no cabeçalho de resposta.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    // Nota: o eco do cabeçalho de resposta depende do callback OnStarting, que o
    // DefaultHttpContext não dispara em isolamento; esse comportamento é coberto
    // pelo teste de pipeline HTTP (CorrelationIdEndpointTests). Aqui validamos a
    // resolução do identificador e sua propagação ao TraceIdentifier.

    [Fact]
    public async Task WhenHeaderProvided_PreservesItAsTraceIdentifier()
    {
        const string provided = "abc-123-correlation";
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = provided;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        Assert.Equal(provided, context.TraceIdentifier);
    }

    [Fact]
    public async Task WhenHeaderMissing_GeneratesCorrelationId()
    {
        var context = new DefaultHttpContext();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        Assert.False(string.IsNullOrWhiteSpace(context.TraceIdentifier));
    }

    [Fact]
    public async Task WhenHeaderBlank_GeneratesCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "   ";

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        Assert.False(string.IsNullOrWhiteSpace(context.TraceIdentifier));
        Assert.NotEqual("   ", context.TraceIdentifier);
    }
}
