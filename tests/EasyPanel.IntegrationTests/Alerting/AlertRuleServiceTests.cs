using EasyPanel.Infrastructure.Alerting;
using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Alerting;

/// <summary>
/// Testes do <see cref="AlertRuleService"/> (Task 3.1 — R1.1-R1.7): CRUD, validação
/// de tipos de evento/limiar/canais, validação de escopo cross-tenant → 404,
/// desativação preserva a regra, e auditoria de create/update.
/// </summary>
public sealed class AlertRuleServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3");
    private static readonly Guid LocationA = Guid.Parse("d4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private Guid _printerIdTenantA;
    private Guid _printerIdTenantB;

    public AlertRuleServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedTenants();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Create_WithValidData_Succeeds_AndAudits()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Falha de coleta",
                "Alerta quando uma coleta falha",
                [2],
                AlertRuleScopeType.Tenant,
                null,
                null,
                null,
                AlertSeverity.Atencao,
                null,
                null,
                true,
                false,
                null,
                false,
                null,
                null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsActive);
        Assert.Equal(TenantA, result.Value.TenantId);
        Assert.Contains(_audit.Entries, e => e.Action == "alertrule.create");
    }

    [Fact]
    public async Task Create_WithoutEventTypes_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra inválida", null, [], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Informativa, null, null, true, false, null, false, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.EventTypesRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_ThresholdWithoutWindow_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra com limiar incompleto", null, [3], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Critica, 5, null, true, false, null, false, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.InvalidThreshold.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_ScopedToPrinterOfAnotherTenant_IsInvalidScope()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra cross-tenant", null, [2], AlertRuleScopeType.Printer, null, _printerIdTenantB, null,
                AlertSeverity.Critica, null, null, true, false, null, false, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.InvalidScope.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_ScopedToPrinterOfOwnTenant_Succeeds()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra por impressora", null, [2], AlertRuleScopeType.Printer, null, _printerIdTenantA, null,
                AlertSeverity.Critica, null, null, true, false, null, false, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_printerIdTenantA, result.Value.ScopePrinterId);
    }

    [Fact]
    public async Task Create_EmailEnabledWithoutRecipients_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra e-mail sem destinatário", null, [2], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Critica, null, null, true, true, null, false, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.EmailRecipientsRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_WebhookNotHttps_Fails()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra webhook http", null, [2], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Critica, null, null, true, false, null, true, "http://example.com/hook", "segredo"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.WebhookUrlNotHttps.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_WebhookHttps_Succeeds_AndSecretNeverInDto()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra webhook https", null, [2], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Critica, null, null, true, false, null, true, "https://example.com/hook", "segredo-super-secreto"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://example.com/hook", result.Value.WebhookUrl);

        // AlertRuleDto não declara WebhookSecret: nenhuma serialização o exporia.
        Assert.DoesNotContain("segredo-super-secreto", result.Value.ToString());
    }

    [Fact]
    public async Task Update_Deactivate_PreservesRule()
    {
        var service = CreateService();
        var created = await service.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra a desativar", null, [3], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Atencao, null, null, true, false, null, false, null, null),
            CancellationToken.None);

        var updated = await service.UpdateAsync(
            created.Value.Id,
            new UpdateAlertRuleRequest(
                created.Value.Name, created.Value.Description, false, [3], AlertRuleScopeType.Tenant,
                null, null, null, AlertSeverity.Atencao, null, null, true, false, null, false, null, null),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.False(updated.Value.IsActive);
        Assert.Contains(_audit.Entries, e => e.Action == "alertrule.update");

        var stillThere = await service.GetAsync(created.Value.Id, CancellationToken.None);
        Assert.True(stillThere.IsSuccess);
    }

    [Fact]
    public async Task Get_CrossTenant_ReturnsNotFound()
    {
        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();
        var created = await serviceB.CreateAsync(
            new CreateAlertRuleRequest(
                "Regra do tenant B", null, [2], AlertRuleScopeType.Tenant, null, null, null,
                AlertSeverity.Critica, null, null, true, false, null, false, null, null),
            CancellationToken.None);

        _tenantContext.SetTenant(TenantA);
        var serviceA = CreateService();
        var result = await serviceA.GetAsync(created.Value.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AlertingErrors.NotFound.Code, result.Error.Code);
    }

    private AlertRuleService CreateService() => new(CreateContext(), new FakeCurrentUser(), _audit);

    private void SeedTenants()
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

        var customerB = Guid.NewGuid();
        var locationB = Guid.NewGuid();
        context.Set<Customer>().Add(new Customer
        {
            Id = customerB,
            TenantId = TenantB,
            RazaoSocial = "Cliente B",
            Cnpj = "04252011000110",
            Status = CustomerStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Set<Location>().Add(new Location
        {
            Id = locationB,
            TenantId = TenantB,
            CustomerId = customerB,
            Nome = "Local B",
            Status = LocationStatus.Ativo,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        _printerIdTenantB = Guid.NewGuid();
        context.Set<Printer>().Add(new Printer
        {
            Id = _printerIdTenantB,
            TenantId = TenantB,
            CustomerId = customerB,
            LocationId = locationB,
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
