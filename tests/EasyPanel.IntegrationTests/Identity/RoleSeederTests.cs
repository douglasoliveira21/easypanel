using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.IntegrationTests.Identity;

/// <summary>
/// Testes do <see cref="RoleSeeder"/> (Task 4.1 / R5.1) sobre o provider
/// relacional SQLite in-memory, reutilizando o <see cref="AppDbContext"/> real e
/// o registro de Identity de <see cref="IdentityServiceCollectionExtensions.AddPlatformIdentity"/>.
///
/// Verifica que a semeadura cria os 8 papéis e é idempotente (executar novamente
/// não duplica papéis).
/// </summary>
public sealed class RoleSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public RoleSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();

        // Super Admin evita interferência de filtros de tenant na materialização.
        var tenantContext = new NullSuperAdminTenantContext();
        services.AddSingleton<ITenantContext>(tenantContext);

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddPlatformIdentity();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SeedAsync_CreatesAllEightRoles()
    {
        using var scope = _provider.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IRoleSeeder>();

        var created = await seeder.SeedAsync();

        Assert.Equal(8, created);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var roleName in Roles.All)
        {
            Assert.True(await roleManager.RoleExistsAsync(roleName), $"Papel ausente: {roleName}");
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        // Primeira execução cria os 8 papéis.
        using (var scope = _provider.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IRoleSeeder>();
            Assert.Equal(8, await seeder.SeedAsync());
        }

        // Segunda execução não cria nada (idempotente).
        using (var scope = _provider.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IRoleSeeder>();
            Assert.Equal(0, await seeder.SeedAsync());
        }

        // Não há papéis duplicados após duas execuções.
        using (var scope = _provider.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var count = await roleManager.Roles.CountAsync();
            Assert.Equal(8, count);
        }
    }

    /// <summary>
    /// <see cref="ITenantContext"/> em modo Super Admin sem tenant, para não
    /// aplicar filtros na materialização dos papéis de Identity.
    /// </summary>
    private sealed class NullSuperAdminTenantContext : ITenantContext
    {
        public Guid? TenantId => null;

        public bool IsSuperAdmin => true;

        public bool HasTenant => false;
    }
}
