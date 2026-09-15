using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.IntegrationTests.Auditing;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dos testes do endpoint
/// <c>GET /api/v1/audit-logs</c> (Task 5.2 / R10.5, R12.4). Como as demais
/// factories de integração, substitui a persistência real por SQLite in-memory e
/// emite tokens reais; adicionalmente:
/// <list type="bullet">
///   <item>semeia os papéis da Fase 1 e atribui papéis aos usuários (Administrador
///     tem <c>audit.view</c>; Operacional não), para exercitar 200 vs 403;</item>
///   <item>semeia registros de auditoria em <b>dois tenants</b>, para provar o
///     isolamento por tenant na leitura (R10.5).</item>
/// </list>
/// </summary>
public sealed class AuditLogsWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string KnownPassword = "Str0ng!Passw0rd";

    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>Administrador do Tenant A: possui <c>audit.view</c>.</summary>
    public const string AdminAEmail = "admin@tenant-a.example.com";

    /// <summary>Operacional do Tenant A: não possui <c>audit.view</c>.</summary>
    public const string OperationalAEmail = "operational@tenant-a.example.com";

    /// <summary>Quantidade de registros de auditoria semeados no Tenant A.</summary>
    public const int TenantAAuditRows = 150;

    /// <summary>Quantidade de registros de auditoria semeados no Tenant B.</summary>
    public const int TenantBAuditRows = 3;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AuditLogsWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Database:ConnectionString"] =
                    "Host=127.0.0.1;Port=1;Database=easypanel_test;Username=none;Password=none",
                ["Jwt:Issuer"] = "easypanel-integration",
                ["Jwt:Audience"] = "easypanel-integration",
                ["Jwt:SigningKey"] = "integration-tests-signing-key-not-a-secret-32b+",
            };

            configuration.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            RemoveEntityFrameworkRegistrations(services);
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // Refaz o registro do IDbContextFactory<AppDbContext> descartado pela
            // remoção do EF, usado pela trilha de auditoria isolada (Task 5.2).
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.AddSingleton<IDbContextFactory<AppDbContext>>(_ =>
                new AuditDbContextFactory(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options));

            using var scope = services.BuildServiceProvider().CreateScope();
            SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        });
    }

    private static void RemoveEntityFrameworkRegistrations(IServiceCollection services)
    {
        var toRemove = services
            .Where(d =>
                d.ServiceType == typeof(AppDbContext)
                || d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || (d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext)))
                || (d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true))
            .ToList();

        foreach (var descriptor in toRemove)
        {
            services.Remove(descriptor);
        }
    }

    private async Task SeedAsync(IServiceProvider provider)
    {
        var context = provider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();

        var roleSeeder = provider.GetRequiredService<IRoleSeeder>();
        await roleSeeder.SeedAsync();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await CreateUserWithRoleAsync(userManager, AdminAEmail, TenantA, Roles.Administrador);
        await CreateUserWithRoleAsync(userManager, OperationalAEmail, TenantA, Roles.Operacional);

        // Semeia a trilha em dois tenants para provar o isolamento por tenant
        // (R10.5) e uma contagem suficiente para paginar (> 100 — R12.4).
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < TenantAAuditRows; i++)
        {
            context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                Action = "customer.update",
                ResourceType = "Customer",
                ResourceId = $"a-{i:D4}",
                Result = AuditResult.Success,
                OccurredAt = now.AddSeconds(i),
                CreatedAt = now,
            });
        }

        for (var i = 0; i < TenantBAuditRows; i++)
        {
            context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = TenantB,
                Action = "user.create",
                ResourceType = "User",
                ResourceId = $"b-{i:D4}",
                Result = AuditResult.Success,
                OccurredAt = now.AddSeconds(i),
                CreatedAt = now,
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task CreateUserWithRoleAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        Guid tenantId,
        string roleName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            TenantId = tenantId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var create = await userManager.CreateAsync(user, KnownPassword);
        if (!create.Succeeded)
        {
            var errors = string.Join("; ", create.Errors.Select(e => $"{e.Code}:{e.Description}"));
            throw new InvalidOperationException($"Falha ao semear usuário {email}: {errors}");
        }

        var addRole = await userManager.AddToRoleAsync(user, roleName);
        if (!addRole.Succeeded)
        {
            var errors = string.Join("; ", addRole.Errors.Select(e => $"{e.Code}:{e.Description}"));
            throw new InvalidOperationException($"Falha ao atribuir papel {roleName} a {email}: {errors}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
