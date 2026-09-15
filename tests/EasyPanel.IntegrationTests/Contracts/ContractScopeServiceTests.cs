using EasyPanel.Infrastructure.Contracts;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Contracts;

/// <summary>
/// Testes do <see cref="ContractScopeService"/> (Task 3.2 — R2): Local/
/// Impressora de Cliente diferente → 400; conflito de vigência sobreposta →
/// 409; remoção; listagem restrita ao contrato/tenant.
/// </summary>
public sealed class ContractScopeServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid CustomerOther = Guid.Parse("f6f6f6f6-6666-6666-6666-f6f6f6f6f6f6");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid LocationOther = Guid.Parse("07070707-7777-7777-7777-070707070707");
    private static readonly Guid PrinterA = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");
    private static readonly Guid PrinterOther = Guid.Parse("08080808-8888-8888-8888-080808080808");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-15T00:00:00Z"));

    public ContractScopeServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedTenant();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AddLocation_SameCustomer_Succeeds()
    {
        var (scope, contract) = await CreateScopeAndContractAsync();

        var result = await scope.AddLocationAsync(contract.Id, LocationA, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(_audit.Entries, e => e.Action == "contract.scope_add");
    }

    [Fact]
    public async Task AddLocation_DifferentCustomer_Fails()
    {
        var (scope, contract) = await CreateScopeAndContractAsync();

        var result = await scope.AddLocationAsync(contract.Id, LocationOther, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.ScopeCustomerMismatch.Code, result.Error.Code);
    }

    [Fact]
    public async Task AddPrinter_DifferentCustomer_Fails()
    {
        var (scope, contract) = await CreateScopeAndContractAsync();

        var result = await scope.AddPrinterAsync(contract.Id, PrinterOther, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.ScopeCustomerMismatch.Code, result.Error.Code);
    }

    [Fact]
    public async Task AddPrinter_OverlappingVigenciaOnAnotherContract_Fails()
    {
        var contractService = CreateContractService();
        var scope = CreateScopeService();

        var first = await CreateContractAsync(contractService, "C-First");
        await scope.AddPrinterAsync(first.Id, PrinterA, CancellationToken.None);

        var second = await CreateContractAsync(contractService, "C-Second");
        var result = await scope.AddPrinterAsync(second.Id, PrinterA, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.ScopeOverlap.Code, result.Error.Code);
    }

    [Fact]
    public async Task AddPrinter_NonOverlappingVigencia_Succeeds()
    {
        var contractService = CreateContractService();
        var scope = CreateScopeService();

        var first = await contractService.CreateAsync(
            new CreateContractRequest("C-First", CustomerA, Jan(1), Jan(31), null), CancellationToken.None);
        await scope.AddPrinterAsync(first.Value.Id, PrinterA, CancellationToken.None);

        var second = await contractService.CreateAsync(
            new CreateContractRequest("C-Second", CustomerA, Feb(1), null, null), CancellationToken.None);
        var result = await scope.AddPrinterAsync(second.Value.Id, PrinterA, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RemovePrinter_ThenListIsEmpty()
    {
        var (scope, contract) = await CreateScopeAndContractAsync();
        await scope.AddPrinterAsync(contract.Id, PrinterA, CancellationToken.None);

        var remove = await scope.RemovePrinterAsync(contract.Id, PrinterA, CancellationToken.None);
        Assert.True(remove.IsSuccess);

        var list = await scope.ListPrintersAsync(contract.Id, CancellationToken.None);
        Assert.Empty(list.Value);
    }

    [Fact]
    public async Task ListLocations_CrossTenant_NotFound()
    {
        var (scope, contract) = await CreateScopeAndContractAsync();

        _tenantContext.SetTenant(Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2"));
        var scopeB = new ContractScopeService(CreateContext(), new FakeCurrentUser(), _audit, _clock);

        var result = await scopeB.ListLocationsAsync(contract.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.NotFound.Code, result.Error.Code);
    }

    private static DateTimeOffset Jan(int day) => new(2026, 1, day, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Feb(int day) => new(2026, 2, day, 0, 0, 0, TimeSpan.Zero);

    private async Task<(ContractScopeService Scope, ContractDto Contract)> CreateScopeAndContractAsync()
    {
        var contractService = CreateContractService();
        var scope = CreateScopeService();
        var contract = await CreateContractAsync(contractService);
        return (scope, contract);
    }

    private async Task<ContractDto> CreateContractAsync(IContractService service, string number = "C-001")
    {
        var result = await service.CreateAsync(new CreateContractRequest(number, CustomerA, Jan(1), null, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private ContractService CreateContractService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private ContractScopeService CreateScopeService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private void SeedTenant()
    {
        using var context = CreateContext();
        context.Set<Customer>().Add(new Customer
        {
            Id = CustomerA,
            TenantId = TenantA,
            RazaoSocial = "Cliente A",
            Cnpj = "11222333000181",
            Status = CustomerStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Customer>().Add(new Customer
        {
            Id = CustomerOther,
            TenantId = TenantA,
            RazaoSocial = "Cliente Outro",
            Cnpj = "11222333000260",
            Status = CustomerStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Location>().Add(new Location
        {
            Id = LocationA,
            TenantId = TenantA,
            CustomerId = CustomerA,
            Nome = "Local A",
            Status = LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Location>().Add(new Location
        {
            Id = LocationOther,
            TenantId = TenantA,
            CustomerId = CustomerOther,
            Nome = "Local Outro",
            Status = LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Printer>().Add(new Printer
        {
            Id = PrinterA,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Printer>().Add(new Printer
        {
            Id = PrinterOther,
            TenantId = TenantA,
            CustomerId = CustomerOther,
            LocationId = LocationOther,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId => Guid.Parse("99999999-9999-9999-9999-999999999999");
    }

    private sealed class FakeAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task LogAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _current = start;

        public override DateTimeOffset GetUtcNow() => _current = _current.AddTicks(1);
    }

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
