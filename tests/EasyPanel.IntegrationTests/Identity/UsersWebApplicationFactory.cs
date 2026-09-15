using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.IntegrationTests.Identity;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dos testes dos endpoints
/// <c>/api/v1/users</c> (Task 6.2 / R7.1, R7.3, R7.4, R7.6, R7.7, R7.8, R12.1,
/// R12.2). Como as demais factories de integração, substitui a persistência real
/// por SQLite in-memory e emite tokens reais; adicionalmente:
/// <list type="bullet">
///   <item>semeia os papéis da Fase 1 e atribui papéis aos usuários semeados
///     (Administrador tem <c>user.manage</c>; Operacional não), para exercitar
///     200/201 vs 403;</item>
///   <item>semeia um administrador em <b>dois tenants</b> distintos, para provar o
///     isolamento por tenant (R7.7) e a recusa cross-tenant como 404 (R7.3).</item>
/// </list>
/// </summary>
public sealed class UsersWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Senha válida (política R3.7) usada pelos usuários semeados.</summary>
    public const string KnownPassword = "Str0ng!Passw0rd";

    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>Administrador do Tenant A: possui <c>user.manage</c>.</summary>
    public const string AdminAEmail = "admin@tenant-a.example.com";

    /// <summary>Operacional do Tenant A: não possui <c>user.manage</c>.</summary>
    public const string OperationalAEmail = "operational@tenant-a.example.com";

    /// <summary>Administrador do Tenant B: possui <c>user.manage</c> no seu tenant.</summary>
    public const string AdminBEmail = "admin@tenant-b.example.com";

    /// <summary>Id do administrador do Tenant A (para asserções de ator na auditoria — R7.8).</summary>
    public Guid AdminAId { get; private set; }

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public UsersWebApplicationFactory()
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
            // remoção do EF, usado pela trilha de auditoria isolada (Task 5.2), à
            // qual a auditoria das mutações de usuário é escrita (R7.8).
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.AddSingleton<IDbContextFactory<AppDbContext>>(_ =>
                new AuditDbContextFactory(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options));

            using var scope = services.BuildServiceProvider().CreateScope();
            SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        });
    }

    /// <summary>Abre um escopo de DI para inspeção direta do banco nos testes (auditoria/estado).</summary>
    public IServiceScope CreateDataScope() => Services.CreateScope();

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
        AdminAId = await CreateUserWithRoleAsync(userManager, AdminAEmail, TenantA, Roles.Administrador);
        await CreateUserWithRoleAsync(userManager, OperationalAEmail, TenantA, Roles.Operacional);
        await CreateUserWithRoleAsync(userManager, AdminBEmail, TenantB, Roles.Administrador);
    }

    private static async Task<Guid> CreateUserWithRoleAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        Guid tenantId,
        string roleName)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            // UserName qualificado pelo tenant, coerente com UserService (unicidade
            // global do Identity mapeada à unicidade por tenant do email — R7.2).
            UserName = $"{tenantId:D}:{email}",
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

        return user.Id;
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
