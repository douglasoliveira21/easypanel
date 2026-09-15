using EasyPanel.Infrastructure.Monitoring;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Monitoring;

/// <summary>
/// Testes do <see cref="SupplyService"/> (Task 4.1 — R3.3/R3.4, R4.1/R4.2, R5.1-R5.3,
/// R6.3/R6.5): previsão indisponível/calculada, upsert de limiar, cursor pagination
/// do histórico e isolamento cross-tenant.
/// </summary>
public sealed class SupplyServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a9a9a9a9-a9a9-a9a9-a9a9-a9a9a9a9a9a9");
    private static readonly Guid TenantB = Guid.Parse("b8b8b8b8-b8b8-b8b8-b8b8-b8b8b8b8b8b8");
    private static readonly Guid CustomerA = Guid.Parse("c7c7c7c7-c7c7-c7c7-c7c7-c7c7c7c7c7c7");
    private static readonly Guid LocationA = Guid.Parse("d6d6d6d6-d6d6-d6d6-d6d6-d6d6d6d6d6d6");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
    private Guid _printerId;

    public SupplyServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedPrinter();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetCurrentLevels_WithSingleReading_ForecastUnavailable()
    {
        await SeedReadingAsync(_printerId, "toner-preto", 50, _clock.GetUtcNow());

        var service = CreateService();
        var result = await service.GetCurrentLevelsAsync(_printerId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var level = Assert.Single(result.Value);
        Assert.Equal(50, level.Percent);
        Assert.Null(level.ForecastDepletionAt);
    }

    [Fact]
    public async Task GetCurrentLevels_WithLinearDecline_CalculatesForecast()
    {
        var day0 = _clock.GetUtcNow();
        await SeedReadingAsync(_printerId, "toner-preto", 100, day0);
        await SeedReadingAsync(_printerId, "toner-preto", 80, day0.AddDays(1));
        await SeedReadingAsync(_printerId, "toner-preto", 60, day0.AddDays(2));

        var service = CreateService();
        var result = await service.GetCurrentLevelsAsync(_printerId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var level = Assert.Single(result.Value);
        Assert.Equal(60, level.Percent);
        Assert.NotNull(level.ForecastDepletionAt);

        // Queda de 20%/dia a partir de 60% na última leitura (dia 2) → 0% no dia 5.
        var expected = day0.AddDays(5);
        Assert.True(Math.Abs((level.ForecastDepletionAt!.Value - expected).TotalHours) < 1);
    }

    [Fact]
    public async Task GetCurrentLevels_WithoutDecline_ForecastUnavailable()
    {
        var day0 = _clock.GetUtcNow();
        await SeedReadingAsync(_printerId, "cilindro", 50, day0);
        await SeedReadingAsync(_printerId, "cilindro", 90, day0.AddDays(1));

        var service = CreateService();
        var result = await service.GetCurrentLevelsAsync(_printerId, CancellationToken.None);

        var level = Assert.Single(result.Value);
        Assert.Null(level.ForecastDepletionAt);
    }

    [Fact]
    public async Task SetThreshold_Create_ThenUpdate_IsUpsert_AndAudited()
    {
        var service = CreateService();

        var created = await service.SetThresholdAsync(
            new SetSupplyThresholdRequest(_printerId, "toner-preto", 15), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(15, created.Value.ThresholdPercent);
        Assert.Contains(_audit.Entries, e => e.Action == "supplythreshold.create");

        var updated = await service.SetThresholdAsync(
            new SetSupplyThresholdRequest(_printerId, "toner-preto", 25), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal(created.Value.Id, updated.Value.Id);
        Assert.Equal(25, updated.Value.ThresholdPercent);
        Assert.Contains(_audit.Entries, e => e.Action == "supplythreshold.update");

        var list = await service.ListThresholdsAsync(
            new SupplyThresholdQuery(new PageRequest(1, 10)), CancellationToken.None);
        Assert.Single(list.Value.Items);
    }

    [Fact]
    public async Task SetThreshold_InvalidPercent_Fails()
    {
        var service = CreateService();

        var result = await service.SetThresholdAsync(
            new SetSupplyThresholdRequest(_printerId, "toner-preto", 150), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupplyErrors.InvalidThresholdPercent.Code, result.Error.Code);
    }

    [Fact]
    public async Task DeleteThreshold_Removes_AndAudits()
    {
        var service = CreateService();
        var created = await service.SetThresholdAsync(
            new SetSupplyThresholdRequest(null, null, 10), CancellationToken.None);

        var deleted = await service.DeleteThresholdAsync(created.Value.Id, CancellationToken.None);
        Assert.True(deleted.IsSuccess);
        Assert.Contains(_audit.Entries, e => e.Action == "supplythreshold.delete");

        var list = await service.ListThresholdsAsync(
            new SupplyThresholdQuery(new PageRequest(1, 10)), CancellationToken.None);
        Assert.Empty(list.Value.Items);
    }

    [Fact]
    public async Task ListHistory_CursorPagination_ReturnsPagesWithoutOverlap()
    {
        var day0 = _clock.GetUtcNow();
        for (var i = 0; i < 5; i++)
        {
            await SeedReadingAsync(_printerId, "toner-preto", 100 - (i * 10), day0.AddHours(i));
        }

        var service = CreateService();
        var firstPage = await service.ListHistoryAsync(_printerId, null, null, 2, CancellationToken.None);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await service.ListHistoryAsync(
            _printerId, null, firstPage.Value.NextCursor, 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Value.Items.Count);

        var firstIds = firstPage.Value.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(secondPage.Value.Items, i => firstIds.Contains(i.Id));
    }

    [Fact]
    public async Task GetCurrentLevels_UnknownPrinter_NotFound()
    {
        var service = CreateService();
        var result = await service.GetCurrentLevelsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SupplyErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListThresholds_CrossTenant_DoesNotLeak()
    {
        var serviceA = CreateService();
        await serviceA.SetThresholdAsync(new SetSupplyThresholdRequest(null, null, 10), CancellationToken.None);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();
        var page = await serviceB.ListThresholdsAsync(
            new SupplyThresholdQuery(new PageRequest(1, 10)), CancellationToken.None);

        Assert.True(page.IsSuccess);
        Assert.Empty(page.Value.Items);
    }

    private ISupplyService CreateService() => new SupplyService(CreateContext(), new FakeCurrentUser(), _audit, _clock);

    private async Task SeedReadingAsync(Guid printerId, string label, int percent, DateTimeOffset timestamp)
    {
        using var context = CreateContext();
        context.Set<SupplyReading>().Add(new SupplyReading
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            PrinterId = printerId,
            Label = label,
            Percent = percent,
            Timestamp = timestamp,
            TimestampTicks = timestamp.UtcTicks,
            CreatedAt = timestamp,
        });
        await context.SaveChangesAsync();
    }

    private void SeedPrinter()
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

        _printerId = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = _printerId,
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
