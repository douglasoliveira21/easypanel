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

namespace EasyPanel.IntegrationTests.Customers;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dos testes dos endpoints de
/// locais (Task 8.2 / R9). Substitui a persistência real por SQLite in-memory,
/// emite tokens reais e semeia:
/// <list type="bullet">
///   <item>um Administrador no Tenant A (possui <c>location.view/create/edit</c> e
///     <c>customer.*</c>, usado para criar o cliente-pai nos testes);</item>
///   <item>um Técnico no Tenant A (somente <c>location.view</c> — sem create/edit);</item>
///   <item>um Cliente no Tenant A (sem permissões administrativas);</item>
///   <item>um Administrador no Tenant B, para provar o isolamento por tenant (R9.6/R9.7).</item>
/// </list>
/// </summary>
public sealed class LocationsWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Senha válida (política R3.7) usada pelos usuários semeados.</summary>
    public const string KnownPassword = "Str0ng!Passw0rd";

    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>Administrador do Tenant A: possui location.* e customer.*.</summary>
    public const string AdminAEmail = "admin@tenant-a.example.com";

    /// <summary>Técnico do Tenant A: possui apenas location.view.</summary>
    public const string TecnicoAEmail = "tecnico@tenant-a.example.com";

    /// <summary>Cliente do Tenant A: sem permissões administrativas.</summary>
    public const string ClienteAEmail = "cliente@tenant-a.example.com";

    /// <summary>Administrador do Tenant B: possui location.*/customer.* no seu tenant.</summary>
    public const string AdminBEmail = "admin@tenant-b.example.com";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public LocationsWebApplicationFactory()
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

        var roleSeeder = provider.GetRequiredService<IRoleSeeder>();
        await roleSeeder.SeedAsync();

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        await CreateUserWithRoleAsync(userManager, AdminAEmail, TenantA, Roles.Administrador);
        await CreateUserWithRoleAsync(userManager, TecnicoAEmail, TenantA, Roles.Tecnico);
        await CreateUserWithRoleAsync(userManager, ClienteAEmail, TenantA, Roles.Cliente);
        await CreateUserWithRoleAsync(userManager, AdminBEmail, TenantB, Roles.Administrador);
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
