using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Entities;
using EasyPanel.Shared.Kernel.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Auditing;

/// <summary>
/// Testes de integração da auditoria de recusas de acesso cross-tenant (Task 5.2 /
/// R6.9). O único ponto concreto de recusa de escrita hoje é o interceptor de
/// <c>SaveChanges</c> do <see cref="AppDbContext"/>, que lança
/// <see cref="CrossTenantAccessException"/>. Estes testes exercitam a recusa real
/// e verificam que, quando a fronteira captura a exceção e invoca o
/// <see cref="ICrossTenantAuditor"/>, um registro <c>security.cross_tenant_denied</c>
/// com <see cref="AuditResult.Denied"/> é gravado na trilha.
///
/// <para>
/// A emissão automática em <b>toda</b> tentativa recusada é completada quando o
/// middleware global de erros (Task 9.1) e os serviços de negócio (Tasks 6–8)
/// invocarem este mesmo <see cref="ICrossTenantAuditor"/>. Aqui provamos o
/// contrato: throw + captura na fronteira + gravação do evento.
/// </para>
/// </summary>
public sealed class CrossTenantDenialAuditTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly AuditDbContextFactory _auditFactory;
    private readonly CrossTenantAuditor _auditor;

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public CrossTenantDenialAuditTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _auditFactory = new AuditDbContextFactory(options);
        var logger = new AuditLogger(_auditFactory, TimeProvider.System);
        _auditor = new CrossTenantAuditor(logger);

        _tenantContext.SetTenant(TenantA);
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task CrossTenantWrite_WhenDeniedAndAudited_WritesDeniedAuditRow()
    {
        // Tenta gravar uma entidade marcada para outro tenant a partir do contexto
        // do Tenant A: o interceptor recusa com CrossTenantAccessException (R6.6).
        _tenantContext.SetTenant(TenantA);
        var denied = false;

        try
        {
            using var context = CreateContext();
            var gadget = new TestGadget { Id = Guid.NewGuid(), Name = "spoof", TenantId = TenantB };
            context.Set<TestGadget>().Add(gadget);
            context.SaveChanges();
        }
        catch (CrossTenantAccessException)
        {
            // Fronteira captura a recusa e registra o evento (R6.9). É o mesmo
            // helper que o middleware de erros (9.1) e os serviços (6–8) invocarão.
            denied = true;
            await _auditor.RecordDeniedAsync(
                actorUserId: Actor,
                tenantId: TenantA,
                resourceType: nameof(TestGadget),
                resourceId: null,
                ip: "203.0.113.9",
                userAgent: "xunit",
                CancellationToken.None);
        }

        Assert.True(denied, "A escrita cross-tenant deveria ter sido recusada.");

        using var verify = _auditFactory.CreateDbContext();
        var log = Assert.Single(verify.AuditLogs.ToList());

        Assert.Equal(ICrossTenantAuditor.CrossTenantDeniedAction, log.Action);
        Assert.Equal(AuditResult.Denied, log.Result);
        Assert.Equal(nameof(TestGadget), log.ResourceType);
        Assert.Equal(Actor, log.ActorUserId);
        Assert.Equal(TenantA, log.TenantId);
        Assert.Equal("203.0.113.9", log.Ip);
        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.True(log.OccurredAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task RecordDeniedAsync_WithoutResolvedTenant_StillWritesDeniedRow()
    {
        // Recusa pode ocorrer sem tenant coerente no contexto: o evento ainda é
        // registrado, com TenantId nulo (R6.9 + decisão de TenantId anulável).
        await _auditor.RecordDeniedAsync(
            actorUserId: null,
            tenantId: null,
            resourceType: "Customer",
            resourceId: "cust-1",
            ip: null,
            userAgent: null,
            CancellationToken.None);

        using var verify = _auditFactory.CreateDbContext();
        var log = Assert.Single(verify.AuditLogs.ToList());

        Assert.Equal(ICrossTenantAuditor.CrossTenantDeniedAction, log.Action);
        Assert.Equal(AuditResult.Denied, log.Result);
        Assert.Null(log.TenantId);
        Assert.Null(log.ActorUserId);
        Assert.Equal("cust-1", log.ResourceId);
    }

    private TestGadgetDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new TestGadgetDbContext(options, _tenantContext);
    }

    /// <summary>Entidade de negócio fictícia, exclusiva de teste, isolada por tenant.</summary>
    private sealed class TestGadget : TenantEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Contexto de teste que reutiliza a mecânica de isolamento do <see cref="AppDbContext"/>.</summary>
    private sealed class TestGadgetDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : AppDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestGadget>();
            base.OnModelCreating(modelBuilder);
        }
    }

    /// <summary>Fake controlável de <see cref="ITenantContext"/> para os testes.</summary>
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
    }
}
