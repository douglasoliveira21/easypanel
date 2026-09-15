using EasyPanel.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI dos fluxos de autenticação de sessão (R2, R4): registra
/// <see cref="IAuthService"/> como <c>scoped</c> (depende do
/// <see cref="Microsoft.AspNetCore.Identity.UserManager{TUser}"/> e do
/// <see cref="ITokenService"/>, ambos scoped sobre o <c>AppDbContext</c>).
///
/// Também registra a estrutura MFA-ready (R2.9/R2.10): o
/// <see cref="IMfaTicketService"/> (emissão/validação do <c>mfa_ticket</c>) e o
/// <see cref="IMfaValidator"/> padrão <see cref="DenyAllMfaValidator"/>, que
/// <b>falha fechado</b> — nenhum código é aceito na Fase 1, evitando bypass do
/// segundo fator até que um provedor TOTP real seja registrado.
///
/// Pressupõe que o Identity core (<c>AddPlatformIdentity</c>) e o serviço de
/// tokens (<c>AddTokenService</c>) já estejam registrados.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="IAuthService"/> e a estrutura MFA-ready.</summary>
    public static IServiceCollection AddAuthService(this IServiceCollection services)
    {
        // Emissão/validação do ticket de curta duração (usa JwtOptions; singleton
        // seguro pois é imutável após construção).
        services.AddSingleton<IMfaTicketService, MfaTicketService>();

        // Validador de segundo fator padrão: falha fechada (R2.9). Uma fase futura
        // substitui por um validador TOTP real. Registrado apenas se ninguém já
        // tiver fornecido uma implementação, para permitir override em testes/futuro.
        services.TryAddSingleton<IMfaValidator, DenyAllMfaValidator>();

        // Notificador de redefinição de senha (R3.1). Padrão log-only (não loga o
        // token — R11.2); uma fase futura provê a entrega por email. TryAdd permite
        // override em testes (ex.: captura do token para exercitar o reset E2E).
        services.TryAddSingleton<IPasswordResetNotifier, LogOnlyPasswordResetNotifier>();

        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
