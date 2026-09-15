using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Reporting;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Reporting;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Reporting;

/// <summary>
/// Testes do <see cref="SlaReportService"/> (Task 2.4 — R4): agregação
/// correta de cumprimento de SLA, intervalo sem chamados retorna zeros,
/// isolamento cross-tenant.
/// </summary>
public sealed class SlaReportServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();

    public SlaReportServiceTests()
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

    private static DateTimeOffset Jan(int day) => new(2026, 1, day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_NoTickets_ReturnsZeroedCounts()
    {
        var service = CreateService();

        var result = await service.GetAsync(new SlaReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalTickets);
        Assert.Equal(0, result.Value.FirstResponse.Cumprido);
        Assert.Equal(0m, result.Value.FirstResponse.ComplianceRate);
        Assert.Equal(0, result.Value.Resolution.Cumprido);
        Assert.Equal(0m, result.Value.Resolution.ComplianceRate);
    }

    [Fact]
    public async Task GetAsync_AggregatesComplianceCorrectly()
    {
        AddTicket(SlaComplianceStatus.Cumprido, SlaComplianceStatus.Cumprido, Jan(5));
        AddTicket(SlaComplianceStatus.Cumprido, SlaComplianceStatus.Violado, Jan(10));
        AddTicket(SlaComplianceStatus.Violado, SlaComplianceStatus.Pendente, Jan(15));

        var service = CreateService();
        var result = await service.GetAsync(new SlaReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.Equal(3, result.Value.TotalTickets);

        Assert.Equal(2, result.Value.FirstResponse.Cumprido);
        Assert.Equal(1, result.Value.FirstResponse.Violado);
        Assert.Equal(0, result.Value.FirstResponse.Pendente);
        Assert.Equal(2m / 3m, result.Value.FirstResponse.ComplianceRate);

        Assert.Equal(1, result.Value.Resolution.Cumprido);
        Assert.Equal(1, result.Value.Resolution.Violado);
        Assert.Equal(1, result.Value.Resolution.Pendente);
        Assert.Equal(0.5m, result.Value.Resolution.ComplianceRate);
    }

    [Fact]
    public async Task GetAsync_ExcludesTicketsOutsideRange()
    {
        AddTicket(SlaComplianceStatus.Cumprido, SlaComplianceStatus.Cumprido, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var service = CreateService();
        var result = await service.GetAsync(new SlaReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.Equal(0, result.Value.TotalTickets);
    }

    [Fact]
    public async Task GetAsync_CrossTenant_DoesNotLeak()
    {
        AddTicket(SlaComplianceStatus.Cumprido, SlaComplianceStatus.Cumprido, Jan(5));

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var result = await serviceB.GetAsync(new SlaReportQuery(Jan(1), Jan(31)), CancellationToken.None);

        Assert.Equal(0, result.Value.TotalTickets);
    }

    private void AddTicket(SlaComplianceStatus firstResponse, SlaComplianceStatus resolution, DateTimeOffset createdAt)
    {
        using var context = CreateContext();
        context.Set<Ticket>().Add(new Ticket
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            Title = "Chamado de teste",
            CustomerId = CustomerA,
            Priority = TicketPriority.Media,
            Status = TicketStatus.Aberto,
            RequestedByUserId = Guid.NewGuid(),
            FirstResponseDueAt = createdAt.AddHours(4),
            FirstResponseDueAtTicks = createdAt.AddHours(4).UtcTicks,
            FirstResponseCompliance = firstResponse,
            ResolutionDueAt = createdAt.AddHours(48),
            ResolutionDueAtTicks = createdAt.AddHours(48).UtcTicks,
            ResolutionCompliance = resolution,
            CreatedAtTicks = createdAt.UtcTicks,
            CreatedAt = createdAt,
        });
        context.SaveChanges();
    }

    private SlaReportService CreateService() => new(CreateContext());

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

        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
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
