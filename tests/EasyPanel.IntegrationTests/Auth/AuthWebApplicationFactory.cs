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

namespace EasyPanel.IntegrationTests.Auth;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> dedicada aos testes de
/// autenticação (Task 3.3). Substitui a persistência real (Npgsql/PostgreSQL) por
/// SQLite in-memory (com a conexão mantida aberta pela duração do host) para
/// exercitar o pipeline HTTP completo — autenticação Bearer, Identity, tokens —
/// sem depender de Docker.
///
/// A aplicação de migrações na inicialização é desabilitada
/// (<c>Database:ApplyMigrationsOnStartup=false</c>) e o schema é criado via
/// <c>EnsureCreated</c>. Um <see cref="JwtOptions"/> válido é fornecido pela
/// configuração (chave de assinatura de teste).
///
/// Usuários de teste são semeados via <see cref="UserManager{TUser}"/> (mesmo
/// hasher/políticas de produção), de modo que login/lockout são exercitados de
/// ponta a ponta.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Senha válida (conforme a política R3.7) usada pelos usuários semeados.</summary>
    public const string KnownPassword = "Str0ng!Passw0rd";

    /// <summary>Email do usuário ativo semeado.</summary>
    public const string ActiveUserEmail = "active@tenant-a.example.com";

    /// <summary>Email do usuário inativo semeado.</summary>
    public const string InactiveUserEmail = "inactive@tenant-a.example.com";

    /// <summary>Tenant dos usuários semeados.</summary>
    public static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Notificador de redefinição de senha de teste: captura o último token emitido
    /// (em memória) para que os testes possam recuperá-lo e exercitar o reset E2E,
    /// já que o token só é entregue fora de banda (R3.1).
    /// </summary>
    public CapturingPasswordResetNotifier ResetNotifier { get; } = new();

    // Conexão in-memory mantida aberta: o schema SQLite só sobrevive enquanto a
    // conexão estiver aberta. É fechada no Dispose da factory.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AuthWebApplicationFactory()
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
                // Cadeia presente por conformidade; a persistência real é
                // substituída por SQLite em ConfigureServices.
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
            // Remove o registro do AppDbContext sobre Npgsql (contexto, opções e
            // os serviços internos do provider Npgsql) e o reconfigura sobre a
            // conexão SQLite in-memory compartilhada. Sem limpar os serviços do
            // provider Npgsql, o EF Core detecta dois providers no mesmo container
            // e falha na inicialização.
            RemoveEntityFrameworkRegistrations(services);

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // A remoção de registros do EF (acima) também descarta o
            // IDbContextFactory<AppDbContext> que AddAuditing usa para a trilha
            // isolada. Como o AuthService passou a auditar o login (Task 5.2),
            // refazemos o registro apontando para a mesma conexão SQLite de teste.
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.AddSingleton<IDbContextFactory<AppDbContext>>(_ =>
                new AuditDbContextFactory(
                    new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options));

            // Substitui o notificador padrão (log-only) pela instância capturadora,
            // permitindo aos testes recuperar o token de redefinição emitido.
            services.RemoveAll<IPasswordResetNotifier>();
            services.AddSingleton<IPasswordResetNotifier>(ResetNotifier);

            // Cria o schema e semeia usuários conhecidos.
            using var scope = services.BuildServiceProvider().CreateScope();
            SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
        });
    }

    private static void RemoveEntityFrameworkRegistrations(IServiceCollection services)
    {
        // Remove o contexto e TODAS as configurações de opções do AppDbContext
        // (DbContextOptions e o wrapper IDbContextOptionsConfiguration<AppDbContext>
        // que carrega o UseNpgsql). Manter a configuração original faria o EF Core
        // aplicar dois providers (Npgsql + Sqlite) no mesmo container e falhar.
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

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        await CreateUserAsync(userManager, ActiveUserEmail, isActive: true);
        await CreateUserAsync(userManager, InactiveUserEmail, isActive: false);
    }

    private static async Task CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        bool isActive)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            TenantId = TenantA,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var result = await userManager.CreateAsync(user, KnownPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
            throw new InvalidOperationException($"Falha ao semear usuário {email}: {errors}");
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

/// <summary>
/// Implementação de teste de <see cref="IPasswordResetNotifier"/> que captura o
/// último token de redefinição emitido (por email de destino), simulando o canal
/// de entrega fora de banda. Registrada como singleton para persistir a captura
/// entre a chamada de <c>forgot-password</c> e a de <c>reset-password</c>.
/// </summary>
public sealed class CapturingPasswordResetNotifier : IPasswordResetNotifier
{
    private readonly Dictionary<string, string> _tokensByEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Quantidade de tokens emitidos (invocações de <see cref="SendAsync"/>).</summary>
    public int SentCount { get; private set; }

    /// <inheritdoc />
    public Task SendAsync(ApplicationUser user, string resetToken, CancellationToken ct)
    {
        lock (_gate)
        {
            _tokensByEmail[user.Email!] = resetToken;
            SentCount++;
        }

        return Task.CompletedTask;
    }

    /// <summary>Recupera o último token capturado para o email, ou <c>null</c> se nenhum.</summary>
    public string? TokenFor(string email)
    {
        lock (_gate)
        {
            return _tokensByEmail.TryGetValue(email, out var token) ? token : null;
        }
    }
}
