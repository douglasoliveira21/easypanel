using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Serviço hospedado de bootstrap (seed) da inicialização (tarefa 10.1). Executa
/// <b>após</b> o <see cref="MigrationHostedService"/> (registrado antes na ordem de
/// hosted services) e:
///
/// <list type="bullet">
///   <item>semeia os papéis fixos da plataforma via <see cref="IRoleSeeder"/>
///     (idempotente — R5.1);</item>
///   <item>opcionalmente cria um usuário Super Admin inicial (sem tenant) quando
///     email e senha são fornecidos por configuração/segredo, viabilizando o
///     primeiro acesso/onboarding (R68) — sem dados fictícios.</item>
/// </list>
///
/// <para><b>Guarda.</b> O seed só roda quando <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/>
/// é verdadeiro (implantações reais). Hosts de teste que desabilitam migrações e
/// criam o schema via <c>EnsureCreated</c> não são perturbados por este serviço —
/// eles semeiam o que precisam nas próprias factories.</para>
/// </summary>
public sealed class BootstrapHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DatabaseOptions _databaseOptions;
    private readonly BootstrapOptions _bootstrapOptions;
    private readonly ILogger<BootstrapHostedService> _logger;

    /// <summary>Cria o serviço com o factory de escopo, as opções e o logger.</summary>
    public BootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<BootstrapOptions> bootstrapOptions,
        ILogger<BootstrapHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _databaseOptions = databaseOptions.Value;
        _bootstrapOptions = bootstrapOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_databaseOptions.ApplyMigrationsOnStartup)
        {
            // Sem migrações na inicialização (ex.: ambiente de teste): o seed de
            // bootstrap é responsabilidade de quem provisiona o schema.
            _logger.LogInformation(
                "Bootstrap de inicialização ignorado (ApplyMigrationsOnStartup=false).");
            return;
        }

        using var scope = _scopeFactory.CreateScope();

        if (_bootstrapOptions.SeedRolesOnStartup)
        {
            var roleSeeder = scope.ServiceProvider.GetRequiredService<IRoleSeeder>();
            var created = await roleSeeder.SeedAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Seed de papéis concluído: {Created} papel(éis) criado(s).", created);
        }

        await SeedSuperAdminAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Cria o Super Admin inicial (sem tenant) quando email e senha estão
    /// configurados e ainda não existe um usuário com esse email. Idempotente.
    /// </summary>
    private async Task SeedSuperAdminAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var email = _bootstrapOptions.SuperAdminEmail?.Trim();
        var password = _bootstrapOptions.SuperAdminPassword;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Sem credenciais configuradas: nenhum Super Admin é criado (sem dados
            // fictícios). O onboarding pode criar o primeiro tenant/usuário depois.
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        // Idempotência: não recria se já houver um usuário com esse email.
        var existing = await userManager.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == userManager.NormalizeEmail(email), cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return;
        }

        var superAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            // Super Admin da plataforma não pertence a um tenant específico (R6.7).
            TenantId = null,
            UserName = email,
            Email = email,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var create = await userManager.CreateAsync(superAdmin, password).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            var errors = string.Join("; ", create.Errors.Select(e => $"{e.Code}: {e.Description}"));
            throw new InvalidOperationException($"Falha ao criar o Super Admin inicial: {errors}");
        }

        var addRole = await userManager.AddToRoleAsync(superAdmin, Roles.SuperAdmin).ConfigureAwait(false);
        if (!addRole.Succeeded)
        {
            var errors = string.Join("; ", addRole.Errors.Select(e => $"{e.Code}: {e.Description}"));
            throw new InvalidOperationException($"Falha ao atribuir o papel Super Admin: {errors}");
        }

        _logger.LogInformation("Super Admin inicial criado para onboarding.");
    }
}
