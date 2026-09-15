using System.Text.Json;
using EasyPanel.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace EasyPanel.Api.Middleware;

/// <summary>
/// Middleware de tratamento centralizado de erros (R11.4, R12.2). Converte
/// exceções de domínio e não tratadas em respostas <c>ProblemDetails</c>
/// (RFC 7807), sem vazar detalhes sensíveis (R11.2/R11.4):
///
/// <list type="bullet">
///   <item><see cref="ValidationException"/> → 400 com <c>ValidationProblemDetails</c>
///     (erros por campo).</item>
///   <item><see cref="CrossTenantAccessException"/> / <see cref="NotFoundException"/>
///     → 404 (não revela existência cross-tenant — R6.5).</item>
///   <item><see cref="ConflictException"/> → 409.</item>
///   <item><see cref="ForbiddenException"/> → 403.</item>
///   <item>Qualquer outra <see cref="DomainException"/> → 400.</item>
///   <item>Exceção não tratada → 500 com mensagem genérica; o detalhe real é
///     apenas registrado no log com contexto suficiente (R11.4), nunca no corpo.</item>
/// </list>
///
/// <para>Deve ser registrado cedo no pipeline (após a correlação), envolvendo os
/// componentes subsequentes. O <c>traceId</c> dos <c>ProblemDetails</c> reflete o
/// <see cref="HttpContext.TraceIdentifier"/> (o CorrelationId — R11.3), permitindo
/// correlacionar a resposta ao log.</para>
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>Cria o middleware com o próximo delegate e o logger.</summary>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _logger = logger;
    }

    /// <summary>Executa o pipeline capturando exceções e traduzindo-as em respostas.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            await WriteValidationAsync(context, ex).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            await WriteProblemAsync(context, MapStatusCode(ex), TitleFor(ex), ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Falha inesperada: loga com contexto (sem dados sensíveis) e devolve
            // uma mensagem genérica; o detalhe real nunca vai para o corpo (R11.4).
            _logger.LogError(
                ex,
                "Erro não tratado ao processar {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Erro interno",
                "Ocorreu um erro inesperado ao processar a requisição.")
                .ConfigureAwait(false);
        }
    }

    private static int MapStatusCode(DomainException exception) => exception switch
    {
        CrossTenantAccessException => StatusCodes.Status404NotFound,
        NotFoundException => StatusCodes.Status404NotFound,
        ConflictException => StatusCodes.Status409Conflict,
        ForbiddenException => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(DomainException exception) => exception switch
    {
        CrossTenantAccessException or NotFoundException => "Não encontrado",
        ConflictException => "Conflito",
        ForbiddenException => "Proibido",
        _ => "Requisição inválida",
    };

    private async Task WriteValidationAsync(HttpContext context, ValidationException exception)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Resposta já iniciada; não é possível escrever ValidationProblemDetails.");
            return;
        }

        var problem = new ValidationProblemDetails(
            exception.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Requisição inválida",
            Detail = exception.Message,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";
        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions))
            .ConfigureAwait(false);
    }

    private async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Resposta já iniciada; não é possível escrever ProblemDetails.");
            return;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response
            .WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions))
            .ConfigureAwait(false);
    }
}
