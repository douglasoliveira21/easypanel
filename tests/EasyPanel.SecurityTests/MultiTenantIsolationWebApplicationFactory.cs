using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Identity;
using EasyPanel.Shared.Kernel.Results;
using EasyPanel.Shared.Kernel.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyPanel.SecurityTests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dedicada à suíte consolidada
/// de isolamento multi-tenant (Task 10.2 / R6.5, R6.8, R6.9, R7.2, R8.5).
/// Substitui a persistência real por SQLite in-memory, emite tokens reais e
/// semeia um administrador (com <c>customer.*</c>, <c>location.*</c> e
/// <c>user.manage</c>) em <b>dois tenants distintos</b>, permitindo provar de
/// ponta a ponta que um administrador do Tenant A não acessa recursos do Tenant B.
/// </summary>
public sealed class MultiTenantIsolationWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Senha válida (política R3.7) dos usuários semeados.</summary>
    public const string KnownPassword = "Str0ng!Passw0rd";

    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>Administrador do Tenant A.</summary>
    public const string AdminAEmail = "admin@tenant-a.example.com";

    /// <summary>Administrador do Tenant B.</summary>
    public const string AdminBEmail = "admin@tenant-b.example.com";

    /// <summary>
    /// Técnico do Tenant A: possui <c>alert.view</c>/<c>alert.acknowledge</c> mas
    /// não <c>alert.manage</c> (Fase 3 — R8.2), usado para provar a negação RBAC.
    /// Também possui <c>chamado.view</c>/<c>chamado.manage</c> mas não
    /// <c>sla.manage</c> (Fase 6 — R6.2).
    /// </summary>
    public const string TecnicoAEmail = "tecnico@tenant-a.example.com";

    /// <summary>
    /// Operacional do Tenant A: possui apenas <c>chamado.view</c> (não
    /// <c>chamado.manage</c>), usado para provar a negação RBAC de chamados
    /// (Fase 6 — R6.2). Também possui apenas <c>contrato.view</c> (não
    /// <c>contrato.manage</c>), usado para a negação RBAC de contratos
    /// (Fase 7 — R6.2).
    /// </summary>
    public const string OperacionalAEmail = "operacional@tenant-a.example.com";

    /// <summary>
    /// Financeiro do Tenant A: dono natural do módulo de Contratos (Fase 7),
    /// possui <c>contrato.view</c>+<c>contrato.manage</c>, usado para provar
    /// o caminho positivo de RBAC.
    /// </summary>
    public const string FinanceiroAEmail = "financeiro@tenant-a.example.com";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public MultiTenantIsolationWebApplicationFactory()
    {
        _connection.Open();
    }

    /// <summary>Abre um escopo de DI para inspeção direta do banco (auditoria/estado).</summary>
    public IServiceScope CreateDataScope() => Services.CreateScope();

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
                ["Jwt:Issuer"] = "easypanel-security",
                ["Jwt:Audience"] = "easypanel-security",
                ["Jwt:SigningKey"] = "security-tests-signing-key-not-a-secret-32bytes+",
                // Limite alto para não interferir na sequência de chamadas do teste.
                ["RateLimiting:PermitLimit"] = "10000",
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

            // MinIO não está disponível neste ambiente de teste (Fase 6): substitui
            // o storage real por um fake em memória, para que a suíte de segurança
            // possa exercitar upload/download de anexo de ponta a ponta via HTTP
            // sem depender de um MinIO real.
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage, InMemoryFileStorage>();

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
        await CreateUserAsync(userManager, AdminAEmail, TenantA, Roles.Administrador);
        await CreateUserAsync(userManager, AdminBEmail, TenantB, Roles.Administrador);
        await CreateUserAsync(userManager, TecnicoAEmail, TenantA, Roles.Tecnico);
        await CreateUserAsync(userManager, OperacionalAEmail, TenantA, Roles.Operacional);
        await CreateUserAsync(userManager, FinanceiroAEmail, TenantA, Roles.Financeiro);
    }

    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        Guid tenantId,
        string role)
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

        var addRole = await userManager.AddToRoleAsync(user, role);
        if (!addRole.Succeeded)
        {
            var errors = string.Join("; ", addRole.Errors.Select(e => $"{e.Code}:{e.Description}"));
            throw new InvalidOperationException($"Falha ao atribuir papel a {email}: {errors}");
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

    /// <summary>
    /// Fake em memória de <see cref="IFileStorage"/>, usado no lugar do
    /// <c>MinioFileStorage</c> real nesta suíte (Fase 6) — este ambiente de teste
    /// não tem um MinIO acessível (ver <c>HANDOFF.md</c>).
    /// </summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];

        public Task<Result> UploadAsync(string key, Stream content, string contentType, long sizeBytes, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            _objects[key] = buffer.ToArray();
            return Task.FromResult(Result.Success());
        }

        public Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct) =>
            _objects.TryGetValue(key, out var bytes)
                ? Task.FromResult(Result.Success<Stream>(new MemoryStream(bytes)))
                : Task.FromResult(Result.Failure<Stream>(FileStorageErrors.NotFound));

        public Task<Result> DeleteAsync(string key, CancellationToken ct)
        {
            _objects.Remove(key);
            return Task.FromResult(Result.Success());
        }
    }
}
