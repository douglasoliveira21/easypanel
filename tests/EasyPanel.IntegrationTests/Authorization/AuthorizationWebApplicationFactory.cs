using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.IntegrationTests.Authorization;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dos testes de autorização por
/// permissão (tarefa 4.2 / R5.3–R5.6). Como <c>AuthWebApplicationFactory</c>,
/// substitui a persistência real por SQLite in-memory e emite tokens reais, mas
/// adicionalmente:
/// <list type="bullet">
///   <item>semeia os papéis da Fase 1 (via <c>RoleManager</c>) e atribui papéis
///     específicos aos usuários semeados, para exercitar concede/nega por papel;</item>
///   <item>registra o <see cref="AuthzProbeController"/> (controller exclusivo de
///     teste) como <c>ApplicationPart</c>, sem tocar na superfície de produção.</item>
/// </list>
/// </summary>
public sealed class AuthorizationWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Senha válida (política R3.7) usada pelos usuários semeados.</summary>
    public const string KnownPassword = "Str0ng!Passw0rd";

    /// <summary>Tenant dos usuários semeados.</summary>
    public static readonly Guid TenantA = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Usuário com o papel Administrador (possui user.manage, audit.view, customer.view).</summary>
    public const string AdminEmail = "admin@tenant-a.example.com";

    /// <summary>Usuário com o papel Operacional (possui customer.view, mas não user.manage nem audit.view).</summary>
    public const string OperationalEmail = "operational@tenant-a.example.com";

    /// <summary>Usuário com o papel Cliente (sem permissões administrativas na Fase 1).</summary>
    public const string ClientEmail = "client@tenant-a.example.com";

    /// <summary>Usuário Super Admin (possui todas as permissões do catálogo).</summary>
    public const string SuperAdminEmail = "superadmin@platform.example.com";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AuthorizationWebApplicationFactory()
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

        // Registra o controller exclusivo de teste como ApplicationPart, de modo
        // que suas rotas (/api/v1/_authz-probe/*) existam apenas nos testes.
        builder.ConfigureServices(services =>
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(AuthzProbeController).Assembly);

            RemoveEntityFrameworkRegistrations(services);
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // Refaz o registro do IDbContextFactory<AppDbContext> descartado pela
            // remoção do EF, exigido pela trilha de auditoria que o AuthService
            // agora aciona no login (Task 5.2).
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

    private static async Task SeedAsync(IServiceProvider provider)
    {
        var context = provider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();

        // Semeia os 8 papéis da Fase 1 (mesmo seeder de produção — R5.1).
        var roleSeeder = provider.GetRequiredService<IRoleSeeder>();
        await roleSeeder.SeedAsync();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        await CreateUserWithRoleAsync(userManager, AdminEmail, TenantA, Roles.Administrador);
        await CreateUserWithRoleAsync(userManager, OperationalEmail, TenantA, Roles.Operacional);
        await CreateUserWithRoleAsync(userManager, ClientEmail, TenantA, Roles.Cliente);
        // Super Admin da plataforma: sem tenant.
        await CreateUserWithRoleAsync(userManager, SuperAdminEmail, tenantId: null, Roles.SuperAdmin);
    }

    private static async Task CreateUserWithRoleAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        Guid? tenantId,
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
