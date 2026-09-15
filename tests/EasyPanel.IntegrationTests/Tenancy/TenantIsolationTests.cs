using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Entities;
using EasyPanel.Shared.Kernel.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Tenancy;

/// <summary>
/// Integration tests do isolamento multi-tenant aplicado na camada de ORM
/// (R6.4, R6.5, R6.6). Exercitam, de ponta a ponta sobre o provider relacional
/// SQLite in-memory:
/// <list type="bullet">
///   <item>o filtro global de consulta por <c>TenantId</c> em list/get;</item>
///   <item>a rejeição de update/delete de registro de outro tenant;</item>
///   <item>o preenchimento automático de <c>TenantId</c> em entidades novas.</item>
/// </list>
///
/// Uma entidade e um <see cref="DbContext"/> exclusivos de teste são usados para
/// não poluir o modelo de produção nem exigir migrações; a mecânica de
/// filtro/interceptor herdada do <see cref="AppDbContext"/> é a mesma de produção.
/// </summary>
public sealed class TenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public TenantIsolationTests()
    {
        // A conexão in-memory é mantida aberta pela duração do teste para que o
        // schema criado persista entre operações.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetTenant(TenantA);

        using var seed = CreateContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void List_ReturnsOnlyRowsOfCurrentTenant()
    {
        SeedGadget(TenantA, "a-1");
        SeedGadget(TenantA, "a-2");
        SeedGadget(TenantB, "b-1");

        _tenantContext.SetTenant(TenantA);
        using var context = CreateContext();

        var names = context.Set<TestGadget>().Select(g => g.Name).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "a-1", "a-2" }, names);
    }

    [Fact]
    public void Get_RowOfOtherTenant_IsNotVisible()
    {
        var gadgetB = SeedGadget(TenantB, "b-1");

        _tenantContext.SetTenant(TenantA);
        using var context = CreateContext();

        var found = context.Set<TestGadget>().FirstOrDefault(g => g.Id == gadgetB.Id);

        Assert.Null(found);
    }

    [Fact]
    public void Add_PopulatesTenantIdFromContext()
    {
        _tenantContext.SetTenant(TenantA);

        Guid gadgetId;
        using (var context = CreateContext())
        {
            var gadget = new TestGadget { Id = Guid.NewGuid(), Name = "new" };
            context.Set<TestGadget>().Add(gadget);
            context.SaveChanges();
            gadgetId = gadget.Id;

            // O interceptor preenche o TenantId a partir do contexto (R6.6).
            Assert.Equal(TenantA, gadget.TenantId);
        }

        // Confirma a persistência consultando com o SuperAdmin (sem filtro).
        _tenantContext.SetSuperAdmin();
        using var verify = CreateContext();
        var stored = verify.Set<TestGadget>().Single(g => g.Id == gadgetId);
        Assert.Equal(TenantA, stored.TenantId);
    }

    [Fact]
    public void Update_RowOfOtherTenant_IsRejected()
    {
        var gadgetB = SeedGadget(TenantB, "b-1");

        // Materializa a linha como Super Admin (ignora o filtro), depois muda o
        // contexto para o Tenant A e tenta persistir a alteração.
        _tenantContext.SetSuperAdmin();
        using var context = CreateContext();
        var tracked = context.Set<TestGadget>().Single(g => g.Id == gadgetB.Id);
        tracked.Name = "hijacked";

        _tenantContext.SetTenant(TenantA);

        Assert.Throws<CrossTenantAccessException>(() => context.SaveChanges());
    }

    [Fact]
    public void Delete_RowOfOtherTenant_IsRejected()
    {
        var gadgetB = SeedGadget(TenantB, "b-1");

        _tenantContext.SetSuperAdmin();
        using var context = CreateContext();
        var tracked = context.Set<TestGadget>().Single(g => g.Id == gadgetB.Id);
        context.Set<TestGadget>().Remove(tracked);

        _tenantContext.SetTenant(TenantA);

        Assert.Throws<CrossTenantAccessException>(() => context.SaveChanges());
    }

    [Fact]
    public void Update_RowOfCurrentTenant_IsPersisted()
    {
        var gadgetA = SeedGadget(TenantA, "a-1");

        _tenantContext.SetTenant(TenantA);
        using (var context = CreateContext())
        {
            var tracked = context.Set<TestGadget>().Single(g => g.Id == gadgetA.Id);
            tracked.Name = "renamed";
            context.SaveChanges();
        }

        using var verify = CreateContext();
        var stored = verify.Set<TestGadget>().Single(g => g.Id == gadgetA.Id);
        Assert.Equal("renamed", stored.Name);
    }

    [Fact]
    public void Add_WithDivergentTenantId_IsRejected()
    {
        _tenantContext.SetTenant(TenantA);
        using var context = CreateContext();

        var gadget = new TestGadget { Id = Guid.NewGuid(), Name = "spoof", TenantId = TenantB };
        context.Set<TestGadget>().Add(gadget);

        Assert.Throws<CrossTenantAccessException>(() => context.SaveChanges());
    }

    private TestGadget SeedGadget(Guid tenantId, string name)
    {
        var previous = (_tenantContext.TenantId, _tenantContext.IsSuperAdmin);

        // Grava diretamente no tenant desejado usando o próprio interceptor.
        _tenantContext.SetTenant(tenantId);
        using var context = CreateContext();
        var gadget = new TestGadget { Id = Guid.NewGuid(), Name = name };
        context.Set<TestGadget>().Add(gadget);
        context.SaveChanges();

        // Restaura o contexto anterior.
        if (previous.IsSuperAdmin)
        {
            _tenantContext.SetSuperAdmin(previous.TenantId);
        }
        else if (previous.TenantId is { } tid)
        {
            _tenantContext.SetTenant(tid);
        }

        return gadget;
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

    /// <summary>
    /// Contexto de teste que reutiliza integralmente a mecânica de isolamento do
    /// <see cref="AppDbContext"/> (filtro global + interceptor de escrita) e
    /// adiciona uma entidade <see cref="TestGadget"/> ao modelo.
    /// </summary>
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

        public void SetSuperAdmin(Guid? tenantId = null)
        {
            TenantId = tenantId;
            IsSuperAdmin = true;
        }
    }
}
