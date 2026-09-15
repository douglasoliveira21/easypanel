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
/// Testes do <see cref="ContractService"/> (Task 3.1 — R1/R4/R5): criação com
/// validação de cliente/datas, atualização, transições de status, resolução
/// do contrato aplicável (cascata Impressora → Local → Cliente inteiro),
/// isolamento cross-tenant.
/// </summary>
public sealed class ContractServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid PrinterA = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-15T00:00:00Z"));

    public ContractServiceTests()
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
    public async Task Create_ValidRequest_Persists_AndAudits()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateContractRequest("C-001", CustomerA, Jan(1), null, "Contrato anual"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContractStatus.Rascunho, result.Value.Status);
        Assert.False(result.Value.IsExpired);
        Assert.Contains(_audit.Entries, e => e.Action == "contract.create");
    }

    [Fact]
    public async Task Create_MissingNumber_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateContractRequest(" ", CustomerA, Jan(1), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.NumberRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_InvalidCustomer_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateContractRequest("C-002", Guid.NewGuid(), Jan(1), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.InvalidCustomer.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_EndDateBeforeStartDate_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateContractRequest("C-003", CustomerA, Jan(10), Jan(1), null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.InvalidDateRange.Code, result.Error.Code);
    }

    [Fact]
    public async Task Update_ChangesEditableFields_AndAudits()
    {
        var service = CreateService();
        var created = await CreateContractAsync(service);

        var result = await service.UpdateAsync(
            created.Id, new UpdateContractRequest("C-001-v2", Jan(31), "Atualizado"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("C-001-v2", result.Value.Number);
        Assert.Equal(Jan(31), result.Value.EndDate);
        Assert.Contains(_audit.Entries, e => e.Action == "contract.update");
    }

    [Fact]
    public async Task ChangeStatus_RascunhoToAtivo_Succeeds()
    {
        var service = CreateService();
        var created = await CreateContractAsync(service);

        var result = await service.ChangeStatusAsync(
            created.Id, new ChangeContractStatusRequest(ContractStatus.Ativo), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ContractStatus.Ativo, result.Value.Status);
        Assert.Contains(_audit.Entries, e => e.Action == "contract.status_change");
    }

    [Fact]
    public async Task ChangeStatus_RascunhoToSuspenso_Fails()
    {
        var service = CreateService();
        var created = await CreateContractAsync(service);

        var result = await service.ChangeStatusAsync(
            created.Id, new ChangeContractStatusRequest(ContractStatus.Suspenso), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.InvalidStatusTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task ChangeStatus_EncerradoIsTerminal()
    {
        var service = CreateService();
        var created = await CreateContractAsync(service);
        await service.ChangeStatusAsync(created.Id, new ChangeContractStatusRequest(ContractStatus.Encerrado), CancellationToken.None);

        var result = await service.ChangeStatusAsync(
            created.Id, new ChangeContractStatusRequest(ContractStatus.Ativo), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.InvalidStatusTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task ResolveApplicable_PrefersPrinterScope_OverLocationAndCustomer()
    {
        var service = CreateService();
        var scope = new ContractScopeService(CreateContext(), new FakeCurrentUser(), _audit, _clock);

        var customerWide = await ActivateAsync(service, await CreateContractAsync(service, "C-Customer"));
        var locationScoped = await ActivateAsync(service, await CreateContractAsync(service, "C-Location"));
        await scope.AddLocationAsync(locationScoped.Id, LocationA, CancellationToken.None);
        var printerScoped = await ActivateAsync(service, await CreateContractAsync(service, "C-Printer"));
        await scope.AddPrinterAsync(printerScoped.Id, PrinterA, CancellationToken.None);

        var result = await service.ResolveApplicableAsync(PrinterA, Jan(15), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(printerScoped.Id, result.Value!.Id);

        _ = customerWide;
    }

    [Fact]
    public async Task ResolveApplicable_FallsBackToLocationScope_WhenNoPrinterScope()
    {
        var service = CreateService();
        var scope = new ContractScopeService(CreateContext(), new FakeCurrentUser(), _audit, _clock);

        await ActivateAsync(service, await CreateContractAsync(service, "C-Customer"));
        var locationScoped = await ActivateAsync(service, await CreateContractAsync(service, "C-Location"));
        await scope.AddLocationAsync(locationScoped.Id, LocationA, CancellationToken.None);

        var result = await service.ResolveApplicableAsync(PrinterA, Jan(15), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(locationScoped.Id, result.Value!.Id);
    }

    [Fact]
    public async Task ResolveApplicable_FallsBackToUnscopedCustomerContract()
    {
        var service = CreateService();
        var customerWide = await ActivateAsync(service, await CreateContractAsync(service, "C-Customer"));

        var result = await service.ResolveApplicableAsync(PrinterA, Jan(15), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(customerWide.Id, result.Value!.Id);
    }

    [Fact]
    public async Task ResolveApplicable_IgnoresDraftContract()
    {
        var service = CreateService();
        await CreateContractAsync(service, "C-Draft"); // permanece Rascunho

        var result = await service.ResolveApplicableAsync(PrinterA, Jan(15), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ResolveApplicable_IgnoresExpiredContract()
    {
        var service = CreateService();
        var contract = await service.CreateAsync(
            new CreateContractRequest("C-Expired", CustomerA, Jan(1), Jan(10), null), CancellationToken.None);
        await service.ChangeStatusAsync(contract.Value.Id, new ChangeContractStatusRequest(ContractStatus.Ativo), CancellationToken.None);

        var result = await service.ResolveApplicableAsync(PrinterA, Jan(15), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Get_CrossTenant_NotFound()
    {
        var service = CreateService();
        var created = await CreateContractAsync(service);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.GetAsync(created.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContractErrors.NotFound.Code, result.Error.Code);
    }

    private static DateTimeOffset Jan(int day) => new(2026, 1, day, 0, 0, 0, TimeSpan.Zero);

    private async Task<ContractDto> CreateContractAsync(IContractService service, string number = "C-001")
    {
        var result = await service.CreateAsync(new CreateContractRequest(number, CustomerA, Jan(1), null, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static async Task<ContractDto> ActivateAsync(IContractService service, ContractDto contract)
    {
        var result = await service.ChangeStatusAsync(contract.Id, new ChangeContractStatusRequest(ContractStatus.Ativo), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private ContractService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

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
        context.Set<Location>().Add(new Location
        {
            Id = LocationA,
            TenantId = TenantA,
            CustomerId = CustomerA,
            Nome = "Local A",
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
