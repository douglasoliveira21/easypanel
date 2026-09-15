using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI do esquema de autenticação Bearer (JWT) da plataforma (R2.7).
///
/// Configura <c>AddJwtBearer</c> com <see cref="TokenValidationParameters"/>
/// derivadas das mesmas <see cref="JwtOptions"/> usadas na emissão
/// (<see cref="TokenService"/>): valida emissor, audiência, tempo de vida e chave
/// de assinatura (HMAC-SHA256). Um <see cref="TokenValidationParameters.ClockSkew"/>
/// de zero garante que um JWT expirado seja recusado com HTTP 401 sem tolerância
/// (R2.7).
///
/// <para><b>Resolução tardia das opções.</b> Os parâmetros de validação são
/// construídos a partir de <see cref="IOptions{TOptions}"/> de <see cref="JwtOptions"/>
/// no momento em que o esquema é configurado (via <c>PostConfigure</c>), e não no
/// registro. Isso garante que toda a configuração (incluindo overrides de teste)
/// já esteja vinculada e evita ler uma chave de assinatura ainda vazia.</para>
///
/// <para><b>Mapeamento de claims.</b> O JWT carrega os papéis no claim <c>roles</c>
/// (array JSON) e o tenant no claim <c>tenant_id</c> (ver <see cref="TokenService"/>).
/// Define-se <see cref="TokenValidationParameters.RoleClaimType"/> = <c>roles</c>
/// para que <c>User.IsInRole("Super Admin")</c> funcione e a resolução de tenant
/// opere corretamente. O <see cref="TokenValidationParameters.NameClaimType"/>
/// aponta para <c>sub</c> (Id do usuário). O handler não remapeia claims de
/// entrada (<c>MapInboundClaims = false</c>), preservando os nomes curtos.</para>
/// </summary>
public static class JwtAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registra o esquema de autenticação Bearer padrão validando o JWT de acesso
    /// contra as <see cref="JwtOptions"/> da configuração.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Garante que as JwtOptions estejam vinculadas e validadas (mesma seção
        // usada pela emissão). Idempotente caso AddTokenService já as tenha
        // registrado.
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configura os parâmetros de validação a partir das JwtOptions resolvidas
        // em tempo de execução (após todo o binding de configuração).
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
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
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    // Sem tolerância: token expirado → 401 imediatamente (R2.7).
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = TokenService.RolesClaimType,
                };
            });

        return services;
    }
}
