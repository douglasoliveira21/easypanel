using System.Text.Json;
using EasyPanel.Api.Middleware;
using EasyPanel.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyPanel.IntegrationTests.Api;

/// <summary>
/// Unit tests do <see cref="ExceptionHandlingMiddleware"/> (Task 9.1 / R11.4,
/// R12.2). Exercitam o mapeamento de exceções de domínio e não tratadas em
/// respostas <c>ProblemDetails</c> (RFC 7807), verificando o código de status, o
/// content-type e a ausência de vazamento de detalhes internos em 500.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task NotFoundException_MapsTo404()
    {
        var (status, _) = await RunAsync(_ => throw new NotFoundException("x"));
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task CrossTenantAccessException_MapsTo404()
    {
        var (status, _) = await RunAsync(_ => throw new CrossTenantAccessException());
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task ConflictException_MapsTo409()
    {
        var (status, _) = await RunAsync(_ => throw new ConflictException("dup"));
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task ForbiddenException_MapsTo403()
    {
        var (status, _) = await RunAsync(_ => throw new ForbiddenException("no"));
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task ValidationException_MapsTo400_WithProblemJson()
    {
        var errors = new Dictionary<string, string[]> { ["Email"] = new[] { "obrigatório" } };
        var (status, contentType) = await RunAsync(_ => throw new ValidationException(errors));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.StartsWith("application/problem+json", contentType);
    }

    [Fact]
    public async Task UnhandledException_MapsTo500_WithoutLeakingDetail()
    {
        const string secret = "detalhe interno sensível 12345";
        var (status, body) = await RunCapturingBodyAsync(_ => throw new InvalidOperationException(secret));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        // A mensagem interna real não pode aparecer no corpo (R11.4).
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulRequest_PassesThroughUnchanged()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static async Task<(int Status, string ContentType)> RunAsync(RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        return (context.Response.StatusCode, context.Response.ContentType ?? string.Empty);
    }

    private static async Task<(int Status, string Body)> RunCapturingBodyAsync(RequestDelegate next)
    {
        var context = new DefaultHttpContext();
        var stream = new MemoryStream();
        context.Response.Body = stream;

        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync();

        return (context.Response.StatusCode, body);
    }
}
