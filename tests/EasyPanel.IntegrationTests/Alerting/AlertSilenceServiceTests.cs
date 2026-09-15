using EasyPanel.Infrastructure.Alerting;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Alerting;

/// <summary>
/// Testes do <see cref="AlertSilenceService"/> (Task 3.2 — R7.1-R7.7): validação de
/// escopo/período, encerramento antecipado idempotente (não repetível) e isolamento
/// por tenant.
/// </summary>
public sealed class AlertSilenceServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("e5e5e5e5-e5e5-e5e5-e5e5-e5e5e5e5e5e5");
    private static readonly Guid TenantB = Guid.Parse("f6f6f6f6-f6f6-f6f6-f6f6-f6f6f6f6f6f6");
    private static readonly Guid CustomerA = Guid.Parse("17171717-1717-1717-1717-171717171717");
    private static readonly Guid LocationA = Guid.Parse("28282828-2828-2828-2828-282828282828");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-01-15T12:00:00Z"));
    private Guid _printerIdTenantA;

    public AlertSilenceServiceTests()
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
    public async Task Create_WithoutAnyScope_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertSilenceRequest(null, null, null, _clock.GetUtcNow(), _clock.GetUtcNow().AddHours(1), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.SilenceScopeRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_EndsBeforeStarts_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertSilenceRequest(
                null, _printerIdTenantA, null, _clock.GetUtcNow(), _clock.GetUtcNow().AddHours(-1), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.InvalidSilencePeriod.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_ValidPrinterScope_Succeeds_AndAudits()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertSilenceRequest(
                null, _printerIdTenantA, null, _clock.GetUtcNow(), _clock.GetUtcNow().AddHours(2), "Manutenção programada"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_printerIdTenantA, result.Value.PrinterId);
        Assert.Contains(_audit.Entries, e => e.Action == "alertsilence.create");
    }

    [Fact]
    public async Task EndEarly_Twice_SecondFailsAsAlreadyEnded()
    {
        var service = CreateService();
        var created = await service.CreateAsync(
            new CreateAlertSilenceRequest(
                null, _printerIdTenantA, null, _clock.GetUtcNow(), _clock.GetUtcNow().AddHours(2), null),
            CancellationToken.None);

        var first = await service.EndEarlyAsync(created.Value.Id, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.NotNull(first.Value.EndedEarlyAt);

        var second = await service.EndEarlyAsync(created.Value.Id, CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(AlertingErrors.SilenceAlreadyEnded.Code, second.Error.Code);
    }

    [Fact]
    public async Task List_CrossTenant_DoesNotLeak()
    {
        var serviceA = CreateService();
        await serviceA.CreateAsync(
            new CreateAlertSilenceRequest(
                null, _printerIdTenantA, null, _clock.GetUtcNow(), _clock.GetUtcNow().AddHours(1), null),
            CancellationToken.None);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();
        var page = await serviceB.ListAsync(new AlertSilenceQuery(new PageRequest(1, 10)), CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Empty(page.Value.Items);
    }

    private AlertSilenceService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit, _clock);

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

        _printerIdTenantA = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = _printerIdTenantA,
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
