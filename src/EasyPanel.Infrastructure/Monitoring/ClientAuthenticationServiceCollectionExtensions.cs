using System.Security.Claims;
using System.Text;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Monitoring;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Registro de DI do esquema de autenticação próprio do agente Windows
/// (<see cref="ClientAuthConstants.SchemeName"/>) e do <see cref="IClientContext"/>
/// (R4.1–R4.3, R4.6).
///
/// Adiciona um segundo esquema Bearer que valida os tokens de cliente contra o
/// audience dedicado (<see cref="ClientAuthConstants.Audience"/>), usando a mesma
/// chave de assinatura das <see cref="JwtOptions"/>. No evento
/// <c>OnTokenValidated</c>, resolve o agente/tenant/local <b>exclusivamente</b> dos
/// claims do token e os grava no <see cref="ClientContext"/> scoped — nunca de
/// valores da requisição (R4.2). Tokens inválidos/expirados produzem 401 (R4.3).
/// </summary>
public static class ClientAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="ClientContext"/> scoped, o <see cref="IClientTokenService"/>
    /// e o esquema de autenticação do agente.
    /// </summary>
    public static IServiceCollection AddClientAuthentication(this IServiceCollection services)
    {
        services.AddScoped<ClientContext>();
        services.AddScoped<IClientContext>(sp => sp.GetRequiredService<ClientContext>());
        services.AddScoped<IClientTokenService, ClientTokenService>();
        services.AddScoped<IClientAuthService, ClientAuthService>();
        services.AddScoped<IClientRegistrationService, ClientRegistrationService>();
        services.AddScoped<IHeartbeatService, HeartbeatService>();
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IPrinterService, PrinterService>();
        services.AddScoped<ICounterService, CounterService>();
        services.AddScoped<ISupplyService, SupplyService>();
        services.AddScoped<IClientConfigService, ClientConfigService>();
        services.AddScoped<IClientUpdateService, ClientUpdateService>();

        // Fábrica de contexto de sistema (Super Admin) para operações confiáveis
        // fora do pipeline autenticado (registro de agentes — R3). Reutiliza as
        // DbContextOptions compartilhadas resolvidas do contêiner.
        services.AddSingleton<ISystemDbContextFactory>(sp =>
            new SystemDbContextFactory(
                sp.GetRequiredService<Microsoft.EntityFrameworkCore.DbContextOptions<Persistence.AppDbContext>>()));

        services
            .AddAuthentication()
            .AddJwtBearer(ClientAuthConstants.SchemeName, _ => { });

        services
            .AddOptions<JwtBearerOptions>(ClientAuthConstants.SchemeName)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var options = jwtOptions.Value;
                var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));

                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    // Audience dedicado do agente: um JWT de usuário não autentica aqui.
                    ValidAudience = ClientAuthConstants.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal;
                        var clientContext = context.HttpContext.RequestServices
                            .GetRequiredService<ClientContext>();

                        var clientId = ParseGuid(principal, ClientAuthConstants.ClientIdClaimType);
                        var tenantId = ParseGuid(principal, ClientAuthConstants.TenantIdClaimType);
                        var locationId = ParseGuid(principal, ClientAuthConstants.LocationIdClaimType);
                        var kind = principal?.FindFirstValue(ClientAuthConstants.TokenKindClaimType);

                        // Defesa em profundidade: o token precisa ser de cliente e
                        // carregar identidade completa; caso contrário, falha (401).
                        if (kind != ClientAuthConstants.TokenKindValue
                            || clientId is null || tenantId is null || locationId is null)
                        {
                            context.Fail("Token de cliente inválido.");
                            return Task.CompletedTask;
                        }

                        clientContext.SetClient(clientId.Value, tenantId.Value, locationId.Value);

                        // Propaga o tenant resolvido da identidade do agente ao
                        // TenantContext scoped, para que os filtros globais de
                        // consulta e o interceptor de escrita do AppDbContext
                        // isolem os dados do agente pelo seu tenant (R4.2/R6).
                        var tenantContext = context.HttpContext.RequestServices
                            .GetRequiredService<EasyPanel.Modules.Tenancy.TenantContext>();
                        tenantContext.SetTenant(tenantId.Value);

                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    private static Guid? ParseGuid(ClaimsPrincipal? principal, string claimType)
    {
        var value = principal?.FindFirstValue(claimType);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }
}
