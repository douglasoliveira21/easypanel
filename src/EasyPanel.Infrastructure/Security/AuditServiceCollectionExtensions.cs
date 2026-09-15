using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Registro de DI da trilha de auditoria (R10): vincula <see cref="IAuditLogger"/>
/// ao <see cref="AuditLogger"/> e provê a fábrica de contextos dedicada
/// (<see cref="AuditDbContextFactory"/>) para escritas independentes e atômicas.
///
/// Pressupõe que a persistência (<c>AddPersistence</c>) já esteja registrada, pois
/// a fábrica reutiliza as <see cref="DbContextOptions{AppDbContext}"/> daquele
/// registro.
/// </summary>
public static class AuditServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="IAuditLogger"/> e sua fábrica de contexto.</summary>
    public static IServiceCollection AddAuditing(this IServiceCollection services)
    {
        // TimeProvider é usado para carimbar OccurredAt; garante disponibilidade
        // mesmo quando o host não o registrou explicitamente (override-friendly).
        services.TryAddSingleton(TimeProvider.System);

        // Fábrica dedicada à auditoria: reutiliza as opções do AppDbContext scoped
        // sem conflitar com seu registro. Singleton pois DbContextOptions é imutável.
        services.TryAddSingleton<IDbContextFactory<AppDbContext>>(sp =>
            new AuditDbContextFactory(
                sp.GetRequiredService<DbContextOptions<AppDbContext>>()));

        services.AddScoped<IAuditLogger, AuditLogger>();

        // Seam de auditoria de recusas cross-tenant (R6.9): invocado na fronteira
        // (middleware de erros da tarefa 9.1 e serviços de negócio das tarefas
        // 6–8). Depende apenas do IAuditLogger, evitando o ciclo que surgiria ao
        // injetar auditoria no próprio AppDbContext.
        services.AddScoped<ICrossTenantAuditor, CrossTenantAuditor>();

        // Consulta paginada da trilha, restrita ao tenant do contexto (R10.5).
        // Scoped: usa o AppDbContext scoped da requisição para ler os eventos.
        services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

        return services;
    }
}
