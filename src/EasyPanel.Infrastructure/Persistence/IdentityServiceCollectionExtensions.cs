using EasyPanel.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Registro de DI do ASP.NET Core Identity (núcleo), com stores sobre o
/// <see cref="AppDbContext"/> (PostgreSQL).
///
/// Esta tarefa (3.1) registra apenas os serviços de Identity e configura as
/// políticas de senha (R3.7) e de bloqueio por tentativas (R4.3/R4.4). Emissão de
/// tokens/JWT, fluxo de login e middleware de autenticação são introduzidos nas
/// tarefas 3.2+; portanto nenhum esquema de autenticação é registrado aqui.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Registra o Identity core (<see cref="ApplicationUser"/>/<see cref="ApplicationRole"/>)
    /// com stores no EF Core sobre o <see cref="AppDbContext"/> e provedores de token
    /// padrão, aplicando as políticas de senha e lockout exigidas.
    ///
    /// Política de senha (R3.7): mínimo 8 caracteres exigindo maiúscula, minúscula,
    /// dígito e caractere especial; hash via PBKDF2 do Identity (R2.8).
    ///
    /// Lockout (R4.3/R4.4/R4.5): 5 falhas consecutivas bloqueiam por 15 minutos;
    /// habilitado para novos usuários. O contador é zerado em login bem-sucedido
    /// pelo próprio Identity.
    ///
    /// <c>RequireUniqueEmail</c> permanece <c>false</c> intencionalmente: a unicidade
    /// de email é por tenant (índice composto <c>(TenantId, NormalizedEmail)</c>), não
    /// global.
    /// </summary>
    public static IServiceCollection AddPlatformIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Política de senha (R3.7).
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;

                // Proteção contra força bruta (R4.3/R4.4/R4.5).
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                // Unicidade de email é por tenant, não global (ver índice composto).
                options.User.RequireUniqueEmail = false;

                // O UserName é qualificado pelo tenant ("{tenantId}:{email}") para
                // mapear a unicidade GLOBAL de UserName exigida pelo Identity à
                // unicidade POR TENANT do email (R7.2). O separador ':' não faz
                // parte do conjunto padrão de caracteres permitidos, então é
                // acrescentado aqui para que o UserValidator aceite o formato.
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+:";
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Validade do token de redefinição de senha ≤ 60 minutos (R3.1). O provedor
        // padrão do Identity (DataProtectorTokenProvider) emite tokens sem estado,
        // protegidos por data-protection; sua validade padrão é 1 dia, então é
        // reduzida aqui para 60 min. O uso único decorre da mudança de SecurityStamp
        // aplicada em toda redefinição/alteração de senha, que invalida o token já
        // emitido para usos subsequentes (R3.3).
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromMinutes(60));

        // Semeador idempotente dos papéis fixos da Fase 1 (R5.1). Apenas o registro
        // em DI é feito aqui; a invocação no startup fica a cargo do bootstrap
        // (tarefa 10.1) para não interferir nas factories de teste.
        services.AddScoped<IRoleSeeder, RoleSeeder>();

        return services;
    }
}
