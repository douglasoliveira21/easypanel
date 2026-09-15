using Microsoft.OpenApi;

namespace EasyPanel.Api.Observability;

/// <summary>
/// Configuração da documentação da API via Swagger/OpenAPI (R45), incluindo o
/// esquema de segurança <b>Bearer</b> (JWT), de modo que o Swagger UI permita
/// informar o token de acesso e exercitar os endpoints protegidos.
///
/// <para>Segue a API do Swashbuckle v10 / Microsoft.OpenApi v2: os tipos vivem no
/// namespace <c>Microsoft.OpenApi</c> e o requisito de segurança é declarado via
/// <c>Func&lt;OpenApiDocument, OpenApiSecurityRequirement&gt;</c> referenciando o
/// esquema por <c>OpenApiSecuritySchemeReference</c>.</para>
/// </summary>
public static class SwaggerExtensions
{
    private const string BearerScheme = "bearer";

    /// <summary>
    /// Registra o gerador de Swagger com metadados básicos da API e o esquema de
    /// segurança Bearer aplicado globalmente.
    /// </summary>
    public static IServiceCollection AddPlatformSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "EasyPanel API",
                Version = "v1",
                Description = "API da plataforma EasyPanel (Fase 1).",
            });

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Informe o token JWT no formato: Bearer {token}",
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
            });
        });

        return services;
    }
}
