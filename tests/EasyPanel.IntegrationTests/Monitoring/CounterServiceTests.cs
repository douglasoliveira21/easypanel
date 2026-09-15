using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="CounterService"/> (Task 6.1 — R11.4–R11.7): rejeição de
/// decréscimo, ajuste administrativo auditado (permite reduzir), cursor pagination
/// e isolamento por tenant.
/// </summary>
public sealed class CounterServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("aaaa1111-aaaa-1111-aaaa-111111111111");
    private static readonly Guid CustomerA = Guid.Parse("bbbb2222-bbbb-2222-bbbb-222222222222");
    private static readonly Guid LocationA = Guid.Parse("cccc3333-cccc-3333-cccc-333333333333");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private Guid _printerId;

    public CounterServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        _printerId = SeedPrinter();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Record_Increasing_Succeeds()
    {
        var service = CreateService();

        var r1 = await service.RecordAsync(_printerId, CounterType.BlackAndWhite, null, 100, CounterSource.Api, null, null, CancellationToken.None);
        var r2 = await service.RecordAsync(_printerId, CounterType.BlackAndWhite, null, 200, CounterSource.Api, null, null, CancellationToken.None);

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
    }

    [Fact]
    public async Task Record_Decrease_IsRejected()
    {
        var service = CreateService();
        await service.RecordAsync(_printerId, CounterType.BlackAndWhite, null, 500, CounterSource.Api, null, null, CancellationToken.None);

        var lower = await service.RecordAsync(_printerId, CounterType.BlackAndWhite, null, 100, CounterSource.Api, null, null, CancellationToken.None);

        Assert.True(lower.IsFailure);
        Assert.Equal(MonitoringErrors.CounterDecrease.Code, lower.Error.Code);
    }

    [Fact]
    public async Task Adjust_CanReduce_AndIsAudited()
    {
        var service = CreateService();
        await service.RecordAsync(_printerId, CounterType.BlackAndWhite, null, 1000, CounterSource.Api, null, null, CancellationToken.None);

        var adjust = await service.AdjustAsync(
            new CounterAdjustmentRequest(_printerId, CounterType.BlackAndWhite, null, 50, "Correção de leitura errada"),
            CancellationToken.None);

        Assert.True(adjust.IsSuccess);
        Assert.True(adjust.Value.IsAdministrativeAdjustment);
        Assert.Equal(50, adjust.Value.Value);
        Assert.Contains(_audit.Entries, e => e.Action == "counter.adjust" && e.NewValues == "50");
    }

    [Fact]
    public async Task Adjust_WithoutJustification_Fails()
    {
        var service = CreateService();

        var adjust = await service.AdjustAsync(
            new CounterAdjustmentRequest(_printerId, CounterType.BlackAndWhite, null, 10, "   "),
            CancellationToken.None);

        Assert.True(adjust.IsFailure);
        Assert.Equal(MonitoringErrors.AdjustmentJustificationRequired.Code, adjust.Error.Code);
    }

    [Fact]
    public async Task List_CursorPagination_ReturnsPagesInDescendingOrder()
    {
        var service = CreateService();
        for (long v = 1; v <= 5; v++)
        {
            await service.RecordAsync(_printerId, CounterType.BlackAndWhite, null, v * 10, CounterSource.Api, null, null, CancellationToken.None);
            await Task.Delay(2);
        }

        var firstPage = await service.ListAsync(_printerId, CounterType.BlackAndWhite, null, 2, CancellationToken.None);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await service.ListAsync(
            _printerId, CounterType.BlackAndWhite, firstPage.Value.NextCursor, 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Value.Items.Count);

        // Sem itens repetidos entre as páginas.
        var firstIds = firstPage.Value.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(secondPage.Value.Items, i => firstIds.Contains(i.Id));
    }

    [Fact]
    public async Task List_UnknownPrinter_NotFound()
    {
        var service = CreateService();

        var result = await service.ListAsync(Guid.NewGuid(), null, null, 10, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MonitoringErrors.NotFound.Code, result.Error.Code);
    }

    private CounterService CreateService() =>
        new(CreateContext(), new FakeCurrentUser(), _audit);

    private Guid SeedPrinter()
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

        var id = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = id,
            TenantId = TenantA,
            CustomerId = CustomerA,
            LocationId = LocationA,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
        return id;
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
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
