using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Identity;

/// <summary>
/// Testes de regras de negócio do <see cref="UserService"/> (Task 6.1 / R7.1,
/// R7.2, R7.3, R7.4, R7.6, R7.7) sobre o provider relacional SQLite in-memory,
/// reutilizando o <see cref="AppDbContext"/> real, o <see cref="UserManager{TUser}"/>
/// e o <see cref="RoleManager{TRole}"/> de
/// <see cref="IdentityServiceCollectionExtensions.AddPlatformIdentity"/>, e o
/// <see cref="TokenService"/> real (para observar a revogação de refresh tokens na
/// desativação — R7.4). A escolha por teste de integração é intencional: a lógica
/// depende fortemente do UserManager (política de senha/hash, papéis), que é frágil
/// de simular fielmente com dublês.
///
/// <para><b>Isolamento por tenant.</b> Um <see cref="MutableTenantContext"/>
/// compartilhado permite alternar o tenant do "contexto autenticado" entre as
/// operações, exercitando a criação vinculada ao tenant (R7.1), a unicidade por
/// tenant (R7.2) e a recusa cross-tenant como 404 (R7.3/R7.7).</para>
/// </summary>
public sealed class UserServiceTests : IDisposable
{
    private const string ValidPassword = "Str0ng!Passw0rd";

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly CapturingAuditLogger _auditLogger = new();

    private readonly JwtOptions _jwtOptions = new()
    {
        Issuer = "easypanel-tests",
        Audience = "easypanel-tests",
        SigningKey = "user-service-tests-signing-key-not-a-secret-32b+",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 14,
    };

    public UserServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();

        // AddPlatformIdentity registra AddDefaultTokenProviders, cujo
        // DataProtectorTokenProvider depende de IDataProtectionProvider. O
        // UserManager resolve os provedores de token na construção; portanto o
        // data-protection precisa estar disponível no container de teste.
        services.AddDataProtection();

        services.AddSingleton<ITenantContext>(_tenantContext);
        services.AddSingleton<IAuditLogger>(_auditLogger);
        services.AddSingleton(Options.Create(_jwtOptions));
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddPlatformIdentity();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IUserService, UserService>();

        _provider = services.BuildServiceProvider();

        // Cria o schema e semeia os papéis (Super Admin evita filtros de tenant).
        _tenantContext.SetSuperAdmin();
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Database.EnsureCreated();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var role in Roles.All)
            {
                roleManager.CreateAsync(new ApplicationRole(role)).GetAwaiter().GetResult();
            }
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    // ---- Create: vincula tenant, ativa, atribui papéis (R7.1) --------------

    [Fact]
    public async Task Create_BindsTenantFromContext_ActivatesAndAssignsRoles()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest("alice@tenant-a.example.com", ValidPassword, new[] { Roles.Administrador }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantA, result.Value.TenantId);
        Assert.True(result.Value.IsActive);
        Assert.Equal(new[] { Roles.Administrador }, result.Value.Roles);

        // Auditoria da criação foi emitida (R7.8), sem a senha.
        Assert.Contains(_auditLogger.Entries, e => e.Action == AuditActions.UserCreate);
        Assert.DoesNotContain(_auditLogger.Entries, e =>
            (e.NewValues ?? string.Empty).Contains(ValidPassword, StringComparison.Ordinal));
    }

    // ---- Create: email duplicado no mesmo tenant → 409 (R7.2) --------------

    [Fact]
    public async Task Create_WithDuplicateEmailInSameTenant_ReturnsConflict()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var first = await service.CreateAsync(
            new CreateUserRequest("dup@tenant-a.example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await service.CreateAsync(
            new CreateUserRequest("dup@tenant-a.example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(UserErrors.DuplicateEmail.Code, second.Error.Code);
    }

    // ---- Create: mesmo email em tenant DIFERENTE é permitido (R7.2) --------

    [Fact]
    public async Task Create_WithSameEmailInDifferentTenant_IsAllowed()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        _tenantContext.SetTenant(TenantA);
        var inA = await service.CreateAsync(
            new CreateUserRequest("shared@example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);
        Assert.True(inA.IsSuccess);

        _tenantContext.SetTenant(TenantB);
        var inB = await service.CreateAsync(
            new CreateUserRequest("shared@example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);

        Assert.True(inB.IsSuccess, $"Expected success but got: {inB.Error.Code} / {inB.Error.Message}");
        Assert.Equal(TenantB, inB.Value.TenantId);
        Assert.NotEqual(inA.Value.Id, inB.Value.Id);
    }

    // ---- Create: papel desconhecido → validação (R5.1) --------------------

    [Fact]
    public async Task Create_WithUnknownRole_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest("bob@tenant-a.example.com", ValidPassword, new[] { "NaoExiste" }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(UserErrors.UnknownRole.Code, result.Error.Code);
    }

    // ---- Create: sem tenant no contexto → validação (R7.1) -----------------

    [Fact]
    public async Task Create_WithoutTenantContext_ReturnsValidationError()
    {
        _tenantContext.Clear();
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest("nobody@example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.NoTenantContext.Code, result.Error.Code);
    }

    // ---- Update: altera dados/papéis (R7.3) --------------------------------

    [Fact]
    public async Task Update_ChangesFieldsAndReconcilesRoles()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var created = await service.CreateAsync(
            new CreateUserRequest("carol@tenant-a.example.com", ValidPassword, new[] { Roles.Operacional }),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var updated = await service.UpdateAsync(
            created.Value.Id,
            new UpdateUserRequest(
                "carol.new@tenant-a.example.com",
                MfaEnabled: true,
                new[] { Roles.Administrador, Roles.Supervisor }),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal("carol.new@tenant-a.example.com", updated.Value.Email);
        Assert.True(updated.Value.MfaEnabled);
        Assert.Equal(new[] { Roles.Administrador, Roles.Supervisor }, updated.Value.Roles);

        // Alteração de papéis auditada como evento próprio (R7.8).
        Assert.Contains(_auditLogger.Entries, e => e.Action == AuditActions.UserRolesUpdate);
    }

    // ---- Update: usuário de outro tenant → 404 (R7.3) ----------------------

    [Fact]
    public async Task Update_ForUserOfAnotherTenant_ReturnsNotFound()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        _tenantContext.SetTenant(TenantB);
        var inB = await service.CreateAsync(
            new CreateUserRequest("victim@tenant-b.example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);
        Assert.True(inB.IsSuccess);

        // Ator de outro tenant tenta atualizar o usuário do Tenant B.
        _tenantContext.SetTenant(TenantA);
        var result = await service.UpdateAsync(
            inB.Value.Id,
            new UpdateUserRequest("hijack@tenant-a.example.com", MfaEnabled: false, Array.Empty<string>()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- Deactivate: marca inativo + revoga refresh tokens (R7.4) ----------

    [Fact]
    public async Task Deactivate_SetsInactiveAndRevokesRefreshTokens()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = await service.CreateAsync(
            new CreateUserRequest("dave@tenant-a.example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        // Emite um refresh token ativo para o usuário.
        var issued = await tokenService.IssueRefreshTokenAsync(created.Value.Id, CancellationToken.None);

        var result = await service.DeactivateAsync(created.Value.Id, CancellationToken.None);
        Assert.True(result.IsSuccess);

        // Usuário marcado inativo.
        var stored = await context.Users.AsNoTracking().SingleAsync(u => u.Id == created.Value.Id);
        Assert.False(stored.IsActive);

        // Refresh token do usuário foi revogado (R7.4).
        var token = await context.Set<RefreshToken>().AsNoTracking().SingleAsync(t => t.Id == issued.Token.Id);
        Assert.NotNull(token.RevokedAt);

        Assert.Contains(_auditLogger.Entries, e => e.Action == AuditActions.UserDeactivate);
    }

    // ---- Reactivate: marca ativo (R7.6) ------------------------------------

    [Fact]
    public async Task Reactivate_SetsActive()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var created = await service.CreateAsync(
            new CreateUserRequest("erin@tenant-a.example.com", ValidPassword, Array.Empty<string>()),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        Assert.True((await service.DeactivateAsync(created.Value.Id, CancellationToken.None)).IsSuccess);
        Assert.True((await service.ReactivateAsync(created.Value.Id, CancellationToken.None)).IsSuccess);

        var stored = await context.Users.AsNoTracking().SingleAsync(u => u.Id == created.Value.Id);
        Assert.True(stored.IsActive);

        Assert.Contains(_auditLogger.Entries, e => e.Action == AuditActions.UserReactivate);
    }

    // ---- List: só usuários do tenant, paginado, PageSize ≤ 100 (R7.7) ------

    [Fact]
    public async Task List_ReturnsOnlyCurrentTenantUsers_Paginated()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        // 3 usuários no Tenant A e 2 no Tenant B.
        _tenantContext.SetTenant(TenantA);
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await service.CreateAsync(
                new CreateUserRequest($"a{i}@tenant-a.example.com", ValidPassword, Array.Empty<string>()),
                CancellationToken.None)).IsSuccess);
        }

        _tenantContext.SetTenant(TenantB);
        for (var i = 0; i < 2; i++)
        {
            Assert.True((await service.CreateAsync(
                new CreateUserRequest($"b{i}@tenant-b.example.com", ValidPassword, Array.Empty<string>()),
                CancellationToken.None)).IsSuccess);
        }

        // Lista do Tenant A: só vê os 3 do próprio tenant.
        _tenantContext.SetTenant(TenantA);
        var listA = await service.ListAsync(new PageRequest(Page: 1, PageSize: 25), CancellationToken.None);

        Assert.True(listA.IsSuccess);
        Assert.Equal(3, listA.Value.TotalCount);
        Assert.All(listA.Value.Items, u => Assert.Equal(TenantA, u.TenantId));

        // Paginação: página 1 com PageSize 2 retorna 2 itens, TotalCount 3.
        var page1 = await service.ListAsync(new PageRequest(Page: 1, PageSize: 2), CancellationToken.None);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(3, page1.Value.TotalCount);

        var page2 = await service.ListAsync(new PageRequest(Page: 2, PageSize: 2), CancellationToken.None);
        Assert.Single(page2.Value.Items);
    }

    // ---- List: PageSize é limitado a ≤ 100 (R12.4) -------------------------

    [Fact]
    public async Task List_ClampsPageSizeToMax()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.ListAsync(new PageRequest(Page: 1, PageSize: 1000), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PageRequest.MaxPageSize, result.Value.PageSize);
    }

    // ---- Create: papel Cliente sem CustomerId → validação (Fase 10, R1.1) --

    [Fact]
    public async Task Create_WithClienteRoleWithoutCustomerId_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest("cliente@tenant-a.example.com", ValidPassword, new[] { Roles.Cliente }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(UserErrors.CustomerRequired.Code, result.Error.Code);
    }

    // ---- Create: papel Cliente + outro papel → validação (Fase 10, R1.2) ---

    [Fact]
    public async Task Create_WithClienteRoleCombinedWithAnotherRole_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customerId = await SeedCustomerAsync(context, TenantA);

        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest(
                "cliente-conflito@tenant-a.example.com",
                ValidPassword,
                new[] { Roles.Cliente, Roles.Operacional },
                customerId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(UserErrors.RoleConflict.Code, result.Error.Code);
    }

    // ---- Create: CustomerId de outro tenant → validação (Fase 10, R1.1) ----

    [Fact]
    public async Task Create_WithClienteRoleAndCustomerIdOfAnotherTenant_ReturnsValidationError()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        _tenantContext.SetTenant(TenantB);
        var customerIdInB = await SeedCustomerAsync(context, TenantB);

        _tenantContext.SetTenant(TenantA);
        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest(
                "cliente-cross@tenant-a.example.com",
                ValidPassword,
                new[] { Roles.Cliente },
                customerIdInB),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(UserErrors.CustomerNotFound.Code, result.Error.Code);
    }

    // ---- Create: caminho positivo do vínculo usuário↔Cliente (Fase 10, R1.1) --

    [Fact]
    public async Task Create_WithClienteRoleAndValidCustomerId_PersistsLink()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customerId = await SeedCustomerAsync(context, TenantA);

        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var result = await service.CreateAsync(
            new CreateUserRequest(
                "cliente-ok@tenant-a.example.com", ValidPassword, new[] { Roles.Cliente }, customerId),
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error.Code} / {result.Error.Message}");
        Assert.Equal(customerId, result.Value.CustomerId);
        Assert.Equal(new[] { Roles.Cliente }, result.Value.Roles);
    }

    // ---- Update: retirar o papel Cliente limpa o CustomerId (Fase 10, R1.3) --

    [Fact]
    public async Task Update_RemovingClienteRole_ClearsCustomerId()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customerId = await SeedCustomerAsync(context, TenantA);

        var service = scope.ServiceProvider.GetRequiredService<IUserService>();

        var created = await service.CreateAsync(
            new CreateUserRequest(
                "cliente-troca@tenant-a.example.com", ValidPassword, new[] { Roles.Cliente }, customerId),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var updated = await service.UpdateAsync(
            created.Value.Id,
            new UpdateUserRequest(
                "cliente-troca@tenant-a.example.com",
                MfaEnabled: false,
                new[] { Roles.Operacional },
                CustomerId: customerId),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Null(updated.Value.CustomerId);
        Assert.Equal(new[] { Roles.Operacional }, updated.Value.Roles);
    }

    private static async Task<Guid> SeedCustomerAsync(AppDbContext context, Guid tenantId)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RazaoSocial = "Cliente de Teste Ltda",
            Cnpj = "12345678000190",
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        return customer.Id;
    }

    /// <summary>Fake controlável de <see cref="ITenantContext"/> para alternar o tenant do contexto.</summary>
    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }

        public bool IsSuperAdmin { get; private set; }

        public bool HasTenant => TenantId.HasValue;

        public void SetTenant(Guid tenantId)
        {
            TenantId = tenantId;
            IsSuperAdmin = false;
        }

        public void SetSuperAdmin(Guid? tenantId = null)
        {
            TenantId = tenantId;
            IsSuperAdmin = true;
        }

        public void Clear()
        {
            TenantId = null;
            IsSuperAdmin = false;
        }
    }

    /// <summary>Logger de auditoria em memória; captura as entradas registradas (R7.8).</summary>
    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task LogAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
