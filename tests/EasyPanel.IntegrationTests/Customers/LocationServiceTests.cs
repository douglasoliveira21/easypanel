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
/// Testes de integração do <see cref="LocationService"/> (Task 8.1 / R9.1, R9.2,
/// R9.3, R9.4, R9.5, R9.6, R9.8) sobre o provider relacional SQLite in-memory,
/// reutilizando o <see cref="AppDbContext"/> real (filtro global de tenant +
/// interceptor de escrita). Um <see cref="MutableTenantContext"/> compartilhado
/// permite alternar o tenant do "contexto autenticado" entre as operações,
/// exercitando o isolamento multi-tenant e o vínculo Local→Cliente fim a fim.
/// </summary>
public sealed class LocationServiceTests : IDisposable
{
    private const string ValidCnpj = "11222333000181";
    private const string OtherValidCnpj = "04252011000110";

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly MutableTenantContext _tenantContext = new();

    public LocationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(_tenantContext);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ILocationService, LocationService>();

        _provider = services.BuildServiceProvider();

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

    // ---- Create: local vinculado a cliente do tenant → persistido (R9.1/R9.3) --

    [Fact]
    public async Task Create_WithCustomerOfTenant_PersistsWithTenantAndActiveStatus()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);
        Assert.True(customer.IsSuccess);

        var result = await locations.CreateAsync(
            new CreateLocationRequest(customer.Value.Id, "Matriz"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error.Code}");
        Assert.Equal(TenantA, result.Value.TenantId);
        Assert.Equal(customer.Value.Id, result.Value.CustomerId);
        Assert.Equal("Matriz", result.Value.Nome);
        Assert.Equal(LocationStatus.Ativo, result.Value.Status);
    }

    // ---- Create: múltiplos locais para o mesmo cliente (R9.3) --------------

    [Fact]
    public async Task Create_MultipleLocationsForSameCustomer_AllPersisted()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);
        Assert.True(customer.IsSuccess);

        Assert.True((await locations.CreateAsync(new CreateLocationRequest(customer.Value.Id, "Matriz"), CancellationToken.None)).IsSuccess);
        Assert.True((await locations.CreateAsync(new CreateLocationRequest(customer.Value.Id, "Filial"), CancellationToken.None)).IsSuccess);

        var list = await locations.ListByCustomerAsync(customer.Value.Id, new PageRequest(1, 25), CancellationToken.None);
        Assert.True(list.IsSuccess);
        Assert.Equal(2, list.Value.TotalCount);
    }

    // ---- Create: cliente inexistente → validação (R9.4) --------------------

    [Fact]
    public async Task Create_WithNonExistentCustomer_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var result = await locations.CreateAsync(
            new CreateLocationRequest(Guid.NewGuid(), "Órfão"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(LocationErrors.CustomerNotFound.Code, result.Error.Code);
    }

    // ---- Create: cliente de outro tenant → validação (R9.4, não-vazamento) --

    [Fact]
    public async Task Create_WithCustomerOfAnotherTenant_ReturnsValidationError()
    {
        // Cria um cliente no Tenant B.
        _tenantContext.SetTenant(TenantB);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customerB = await customers.CreateAsync(new CreateCustomerRequest("B Ltda", ValidCnpj), CancellationToken.None);
        Assert.True(customerB.IsSuccess);

        // Tenta criar um local no Tenant A referenciando o cliente do Tenant B.
        _tenantContext.SetTenant(TenantA);
        var result = await locations.CreateAsync(
            new CreateLocationRequest(customerB.Value.Id, "Invasor"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LocationErrors.CustomerNotFound.Code, result.Error.Code);
    }

    // ---- Create: nome ausente → validação (R9.2) --------------------------

    [Fact]
    public async Task Create_WithMissingNome_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);
        Assert.True(customer.IsSuccess);

        var result = await locations.CreateAsync(
            new CreateLocationRequest(customer.Value.Id, "  "),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LocationErrors.MissingNome.Code, result.Error.Code);
    }

    // ---- Update: altera dados; outro tenant → 404 (R9.5/R9.6) --------------

    [Fact]
    public async Task Update_ChangesFields()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);
        var created = await locations.CreateAsync(new CreateLocationRequest(customer.Value.Id, "Old"), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var updated = await locations.UpdateAsync(
            created.Value.Id,
            new UpdateLocationRequest("New", Responsavel: "João", Telefone: "1199999-0000"),
            CancellationToken.None);

        Assert.True(updated.IsSuccess, $"Expected success but got: {updated.Error.Code}");
        Assert.Equal("New", updated.Value.Nome);
        Assert.Equal("João", updated.Value.Responsavel);
        Assert.NotNull(updated.Value.UpdatedAt);
        // O vínculo com o cliente permanece.
        Assert.Equal(customer.Value.Id, updated.Value.CustomerId);
    }

    [Fact]
    public async Task Update_ForLocationOfAnotherTenant_ReturnsNotFound()
    {
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        _tenantContext.SetTenant(TenantB);
        var customerB = await customers.CreateAsync(new CreateCustomerRequest("B Ltda", ValidCnpj), CancellationToken.None);
        var inB = await locations.CreateAsync(new CreateLocationRequest(customerB.Value.Id, "B Local"), CancellationToken.None);
        Assert.True(inB.IsSuccess);

        _tenantContext.SetTenant(TenantA);
        var result = await locations.UpdateAsync(
            inB.Value.Id,
            new UpdateLocationRequest("Hijack"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- Get: local de outro tenant → 404 (R9.6) --------------------------

    [Fact]
    public async Task Get_LocationOfAnotherTenant_ReturnsNotFound()
    {
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        _tenantContext.SetTenant(TenantB);
        var customerB = await customers.CreateAsync(new CreateCustomerRequest("B Ltda", ValidCnpj), CancellationToken.None);
        var inB = await locations.CreateAsync(new CreateLocationRequest(customerB.Value.Id, "B Local"), CancellationToken.None);
        Assert.True(inB.IsSuccess);

        _tenantContext.SetTenant(TenantA);
        var result = await locations.GetAsync(inB.Value.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- ListByCustomer: só locais do cliente/tenant, paginado (R9.7) ------

    [Fact]
    public async Task ListByCustomer_ReturnsOnlyThatCustomersLocations_Paginated()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var c1 = await customers.CreateAsync(new CreateCustomerRequest("C1 Ltda", ValidCnpj), CancellationToken.None);
        var c2 = await customers.CreateAsync(new CreateCustomerRequest("C2 Ltda", OtherValidCnpj), CancellationToken.None);

        // 3 locais para c1, 1 para c2.
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await locations.CreateAsync(new CreateLocationRequest(c1.Value.Id, $"L{i}"), CancellationToken.None)).IsSuccess);
        }

        Assert.True((await locations.CreateAsync(new CreateLocationRequest(c2.Value.Id, "Outro"), CancellationToken.None)).IsSuccess);

        var list = await locations.ListByCustomerAsync(c1.Value.Id, new PageRequest(1, 2), CancellationToken.None);
        Assert.True(list.IsSuccess);
        Assert.Equal(3, list.Value.TotalCount);
        Assert.Equal(2, list.Value.Items.Count);
        Assert.All(list.Value.Items, l => Assert.Equal(c1.Value.Id, l.CustomerId));
    }

    [Fact]
    public async Task ListByCustomer_ClampsPageSizeToMax()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);

        var result = await locations.ListByCustomerAsync(customer.Value.Id, new PageRequest(1, 1000), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PageRequest.MaxPageSize, result.Value.PageSize);
    }

    // ---- ChangeStatus: persiste; valor inválido → validação (R9.8) --------

    [Fact]
    public async Task ChangeStatus_PersistsNewStatus()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);
        var created = await locations.CreateAsync(new CreateLocationRequest(customer.Value.Id, "Matriz"), CancellationToken.None);
        Assert.Equal(LocationStatus.Ativo, created.Value.Status);

        var changed = await locations.ChangeStatusAsync(created.Value.Id, LocationStatus.Inativo, CancellationToken.None);
        Assert.True(changed.IsSuccess);
        Assert.Equal(LocationStatus.Inativo, changed.Value.Status);

        var reloaded = await locations.GetAsync(created.Value.Id, CancellationToken.None);
        Assert.Equal(LocationStatus.Inativo, reloaded.Value.Status);
    }

    [Fact]
    public async Task ChangeStatus_WithOutOfRangeValue_ReturnsValidationError()
    {
        _tenantContext.SetTenant(TenantA);
        using var scope = _provider.CreateScope();
        var customers = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        var locations = scope.ServiceProvider.GetRequiredService<ILocationService>();

        var customer = await customers.CreateAsync(new CreateCustomerRequest("Acme Ltda", ValidCnpj), CancellationToken.None);
        var created = await locations.CreateAsync(new CreateLocationRequest(customer.Value.Id, "Matriz"), CancellationToken.None);

        var result = await locations.ChangeStatusAsync(created.Value.Id, (LocationStatus)999, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LocationErrors.InvalidStatus.Code, result.Error.Code);
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
    }
}
