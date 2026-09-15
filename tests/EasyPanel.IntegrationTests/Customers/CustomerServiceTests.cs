using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyPanel.IntegrationTests.Customers;

/// <summary>
/// Testes de integração do <see cref="CustomerService"/> (Task 7.1 / R8.1, R8.4,
/// R8.5, R8.6, R8.7, R8.8, R8.9) sobre o provider relacional SQLite in-memory,
/// reutilizando o <see cref="AppDbContext"/> real (filtro global de tenant +
/// interceptor de escrita). Um <see cref="MutableTenantContext"/> compartilhado
/// permite alternar o tenant do "contexto autenticado" entre as operações,
/// exercitando o isolamento multi-tenant fim a fim.
/// </summary>
public sealed class CustomerServiceTests : IDisposable
{
    private const string ValidCnpj = "11222333000181";
    private const string ValidCnpjFormatted = "11.222.333/0001-81";
    private const string OtherValidCnpj = "04252011000110";

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly MutableTenantContext _tenantContext = new();

    public CustomerServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(_tenantContext);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ICustomerService, CustomerService>();

        _provider = services.BuildServiceProvider();

        // Cria o schema como Super Admin (sem filtro de tenant na criação).
        _tenantContext.SetSuperAdmin();
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    // ---- Create: CNPJ válido → persistido com tenant do contexto, Ativo (R8.1) --

    [Fact]
    public async Task Create_WithValidCnpj_PersistsWithTenantAndActiveStatus()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var result = await service.CreateAsync(
            new CreateCustomerRequest("Acme Ltda", ValidCnpjFormatted),
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error.Code}");
        Assert.Equal(TenantA, result.Value.TenantId);
        Assert.Equal(CustomerStatus.Ativo, result.Value.Status);
        // CNPJ armazenado normalizado (somente dígitos).
        Assert.Equal(ValidCnpj, result.Value.Cnpj);
    }

    // ---- Create: CNPJ inválido → 400 (R8.4) --------------------------------

    [Fact]
    public async Task Create_WithInvalidCnpj_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var result = await service.CreateAsync(
            new CreateCustomerRequest("Bad Cnpj Ltda", "11.222.333/0001-82"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(CustomerErrors.InvalidCnpj.Code, result.Error.Code);
    }

    // ---- Create: sem tenant no contexto → validação (R8.1) -----------------

    [Fact]
    public async Task Create_WithoutTenantContext_ReturnsValidationError()
    {
        _tenantContext.Clear();
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var result = await service.CreateAsync(
            new CreateCustomerRequest("Orphan Ltda", ValidCnpj),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CustomerErrors.NoTenantContext.Code, result.Error.Code);
    }

    // ---- Create: CNPJ duplicado no mesmo tenant → 409 (R8.5) ---------------

    [Fact]
    public async Task Create_WithDuplicateCnpjInSameTenant_ReturnsConflict()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var first = await service.CreateAsync(
            new CreateCustomerRequest("First Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        // Mesmo CNPJ, ainda que formatado diferente, colide após normalização.
        var second = await service.CreateAsync(
            new CreateCustomerRequest("Second Ltda", ValidCnpjFormatted),
            CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Equal(CustomerErrors.DuplicateCnpj.Code, second.Error.Code);
    }

    // ---- Create: mesmo CNPJ em tenant DIFERENTE é permitido (R8.5) ---------

    [Fact]
    public async Task Create_WithSameCnpjInDifferentTenant_IsAllowed()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        _tenantContext.SetTenant(TenantA);
        var inA = await service.CreateAsync(
            new CreateCustomerRequest("Shared Ltda A", ValidCnpj),
            CancellationToken.None);
        Assert.True(inA.IsSuccess);

        _tenantContext.SetTenant(TenantB);
        var inB = await service.CreateAsync(
            new CreateCustomerRequest("Shared Ltda B", ValidCnpj),
            CancellationToken.None);

        Assert.True(inB.IsSuccess, $"Expected success but got: {inB.Error.Code}");
        Assert.Equal(TenantB, inB.Value.TenantId);
        Assert.NotEqual(inA.Value.Id, inB.Value.Id);
    }

    // ---- Get: escopado ao tenant; outro tenant → 404 (R8.7) ----------------

    [Fact]
    public async Task Get_ForCustomerOfAnotherTenant_ReturnsNotFound()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        _tenantContext.SetTenant(TenantB);
        var inB = await service.CreateAsync(
            new CreateCustomerRequest("Tenant B Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(inB.IsSuccess);

        // Ator do Tenant A não enxerga o cliente do Tenant B.
        _tenantContext.SetTenant(TenantA);
        var result = await service.GetAsync(inB.Value.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Get_ForCustomerOfCurrentTenant_ReturnsCustomer()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await service.CreateAsync(
            new CreateCustomerRequest("Visible Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var result = await service.GetAsync(created.Value.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Value.Id, result.Value.Id);
    }

    // ---- Update: altera dados; outro tenant → 404 (R8.6/R8.7) --------------

    [Fact]
    public async Task Update_ChangesFields()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await service.CreateAsync(
            new CreateCustomerRequest("Old Name Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var updated = await service.UpdateAsync(
            created.Value.Id,
            new UpdateCustomerRequest("New Name Ltda", OtherValidCnpj, NomeFantasia: "New Fantasy"),
            CancellationToken.None);

        Assert.True(updated.IsSuccess, $"Expected success but got: {updated.Error.Code}");
        Assert.Equal("New Name Ltda", updated.Value.RazaoSocial);
        Assert.Equal("New Fantasy", updated.Value.NomeFantasia);
        Assert.Equal(OtherValidCnpj, updated.Value.Cnpj);
        Assert.NotNull(updated.Value.UpdatedAt);
    }

    [Fact]
    public async Task Update_ForCustomerOfAnotherTenant_ReturnsNotFound()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        _tenantContext.SetTenant(TenantB);
        var inB = await service.CreateAsync(
            new CreateCustomerRequest("Victim Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(inB.IsSuccess);

        _tenantContext.SetTenant(TenantA);
        var result = await service.UpdateAsync(
            inB.Value.Id,
            new UpdateCustomerRequest("Hijack Ltda", OtherValidCnpj),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- Update: CNPJ colidindo com outro cliente do tenant → 409 (R8.5) ---

    [Fact]
    public async Task Update_WithCnpjOfAnotherCustomer_ReturnsConflict()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var first = await service.CreateAsync(
            new CreateCustomerRequest("First Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await service.CreateAsync(
            new CreateCustomerRequest("Second Ltda", OtherValidCnpj),
            CancellationToken.None);
        Assert.True(second.IsSuccess);

        // Tenta atualizar o segundo com o CNPJ do primeiro.
        var result = await service.UpdateAsync(
            second.Value.Id,
            new UpdateCustomerRequest("Second Ltda", ValidCnpj),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    // ---- ChangeStatus: persiste novo status (R8.9) -------------------------

    [Fact]
    public async Task ChangeStatus_PersistsNewStatus()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await service.CreateAsync(
            new CreateCustomerRequest("Status Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(CustomerStatus.Ativo, created.Value.Status);

        var changed = await service.ChangeStatusAsync(
            created.Value.Id, CustomerStatus.Bloqueado, CancellationToken.None);

        Assert.True(changed.IsSuccess);
        Assert.Equal(CustomerStatus.Bloqueado, changed.Value.Status);

        // Confirma persistência via nova consulta.
        var reloaded = await service.GetAsync(created.Value.Id, CancellationToken.None);
        Assert.Equal(CustomerStatus.Bloqueado, reloaded.Value.Status);
    }

    // ---- ChangeStatus: valor fora do conjunto fechado → validação (R8.3) ---

    [Fact]
    public async Task ChangeStatus_WithOutOfRangeValue_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var created = await service.CreateAsync(
            new CreateCustomerRequest("Range Ltda", ValidCnpj),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var result = await service.ChangeStatusAsync(
            created.Value.Id, (CustomerStatus)999, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(CustomerErrors.InvalidStatus.Code, result.Error.Code);
    }

    // ---- List: só clientes do tenant, paginado, PageSize ≤ 100 (R8.8/R12.4) --

    [Fact]
    public async Task List_ReturnsOnlyCurrentTenantCustomers_Paginated()
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        // 3 clientes no Tenant A e 2 no Tenant B (CNPJs válidos distintos).
        string[] cnpjsA = { "11222333000181", "04252011000110", "34028316000103" };
        _tenantContext.SetTenant(TenantA);
        for (var i = 0; i < cnpjsA.Length; i++)
        {
            Assert.True((await service.CreateAsync(
                new CreateCustomerRequest($"A{i} Ltda", cnpjsA[i]),
                CancellationToken.None)).IsSuccess);
        }

        string[] cnpjsB = { "11222333000181", "04252011000110" };
        _tenantContext.SetTenant(TenantB);
        for (var i = 0; i < cnpjsB.Length; i++)
        {
            Assert.True((await service.CreateAsync(
                new CreateCustomerRequest($"B{i} Ltda", cnpjsB[i]),
                CancellationToken.None)).IsSuccess);
        }

        // Lista do Tenant A: só os 3 do próprio tenant.
        _tenantContext.SetTenant(TenantA);
        var listA = await service.ListAsync(new PageRequest(Page: 1, PageSize: 25), CancellationToken.None);

        Assert.True(listA.IsSuccess);
        Assert.Equal(3, listA.Value.TotalCount);
        Assert.All(listA.Value.Items, c => Assert.Equal(TenantA, c.TenantId));

        // Paginação: página 1 com PageSize 2 retorna 2 itens; página 2 retorna 1.
        var page1 = await service.ListAsync(new PageRequest(Page: 1, PageSize: 2), CancellationToken.None);
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(3, page1.Value.TotalCount);

        var page2 = await service.ListAsync(new PageRequest(Page: 2, PageSize: 2), CancellationToken.None);
        Assert.Single(page2.Value.Items);
    }

    [Fact]
    public async Task List_ClampsPageSizeToMax()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICustomerService>();

        var result = await service.ListAsync(new PageRequest(Page: 1, PageSize: 1000), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PageRequest.MaxPageSize, result.Value.PageSize);
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
}
