using Microsoft.AspNetCore.Identity;

namespace EasyPanel.Modules.Identity;

/// <summary>
/// Usuário da plataforma baseado em ASP.NET Core Identity com chave <see cref="Guid"/>.
///
/// Além dos campos padrão de <see cref="IdentityUser{TKey}"/> (email normalizado,
/// hash de senha via PBKDF2 — R2.8, contadores de lockout — R4.3, etc.), acrescenta
/// os atributos específicos da plataforma:
/// <list type="bullet">
///   <item><see cref="TenantId"/>: tenant ao qual o usuário pertence. É nulo apenas
///   para o Super Admin da plataforma, que opera fora de um tenant específico.</item>
///   <item><see cref="CustomerId"/>: Cliente (Customer) ao qual o usuário está
///   vinculado (Fase 10 — Portal do Cliente). Não nulo apenas para usuários com o
///   papel <c>Cliente</c>; para os demais, permanece sempre nulo.</item>
///   <item><see cref="IsActive"/>: usuários desativados não podem autenticar.</item>
///   <item><see cref="MfaEnabled"/>: habilita o segundo fator no fluxo de login
///   (estrutural na Fase 1 — R2.9/R2.10).</item>
///   <item><see cref="CreatedAt"/>: carimbo de criação em UTC.</item>
/// </list>
///
/// A unicidade de email é garantida por tenant (índice composto
/// <c>(TenantId, NormalizedEmail)</c>), e não globalmente, pois o mesmo endereço
/// pode existir em tenants distintos.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Tenant do usuário; nulo apenas para o Super Admin da plataforma.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Cliente (Customer) ao qual o usuário está vinculado; não nulo apenas para
    /// usuários com o papel <c>Cliente</c> (Fase 10 — Portal do Cliente).
    /// </summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Indica se o usuário está ativo e pode autenticar.</summary>
    public bool IsActive { get; set; }

    /// <summary>Indica se o usuário exige segundo fator no login (MFA-ready).</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>Momento de criação do usuário, em UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
