using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Security;
using EasyPanel.Modules.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Auditing;

/// <summary>
/// Integration tests do <see cref="AuditLogger"/> sobre o provider relacional
/// SQLite in-memory, exercitando a trilha de auditoria de ponta a ponta (R10.1,
/// R10.3, R10.4):
/// <list type="bullet">
///   <item><c>LogAsync</c> insere um registro com os campos informados, <c>Id</c>
///   atribuído e <c>OccurredAt</c> carimbado (R10.1);</item>
///   <item>eventos com e sem <c>TenantId</c> são ambos persistidos — o caso sem
///   tenant cobre falhas de login anônimas (R10.3 + decisão de <c>TenantId</c>
///   anulável);</item>
///   <item>a auditoria é somente-adição: sucessivas gravações apenas acrescentam
///   linhas, sem alterar as anteriores (R10.4).</item>
/// </list>
///
/// A <see cref="AuditDbContextFactory"/> é construída sobre uma conexão SQLite
/// mantida aberta durante o teste, reutilizando a mesma mecânica de produção.
/// </summary>
public sealed class AuditLoggerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AuditDbContextFactory _factory;
    private readonly AuditLogger _logger;

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public AuditLoggerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _factory = new AuditDbContextFactory(options);
        _logger = new AuditLogger(_factory, TimeProvider.System);

        using var seed = _factory.CreateDbContext();
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task LogAsync_InsertsRow_WithFieldsIdAndOccurredAt()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var entry = new AuditEntry
        {
            ActorUserId = Actor,
            TenantId = TenantA,
            Action = "user.create",
            ResourceType = "User",
            ResourceId = "abc-123",
            NewValues = "{\"email\":\"a@b.com\"}",
            Ip = "203.0.113.7",
            UserAgent = "xunit",
            Result = AuditResult.Success,
        };

        await _logger.LogAsync(entry, CancellationToken.None);

        using var context = _factory.CreateDbContext();
        var stored = Assert.Single(context.AuditLogs.ToList());

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.True(stored.OccurredAt >= before, "OccurredAt deve ser carimbado no momento da gravação.");
        Assert.Equal(Actor, stored.ActorUserId);
        Assert.Equal(TenantA, stored.TenantId);
        Assert.Equal("user.create", stored.Action);
        Assert.Equal("User", stored.ResourceType);
        Assert.Equal("abc-123", stored.ResourceId);
        Assert.Equal("{\"email\":\"a@b.com\"}", stored.NewValues);
        Assert.Equal("203.0.113.7", stored.Ip);
        Assert.Equal("xunit", stored.UserAgent);
        Assert.Equal(AuditResult.Success, stored.Result);
    }

    [Fact]
    public async Task LogAsync_WithTenant_StoresTenantId()
    {
        var entry = new AuditEntry
        {
            ActorUserId = Actor,
            TenantId = TenantA,
            Action = "customer.update",
            ResourceType = "Customer",
            Result = AuditResult.Success,
        };

        await _logger.LogAsync(entry, CancellationToken.None);

        using var context = _factory.CreateDbContext();
        var stored = Assert.Single(context.AuditLogs.ToList());
        Assert.Equal(TenantA, stored.TenantId);
    }

    [Fact]
    public async Task LogAsync_WithoutTenant_StoresNullTenant()
    {
        // Evento sem tenant resolvido (ex.: falha de login anônima) — R10.2/R10.3.
        var entry = new AuditEntry
        {
            ActorUserId = null,
            TenantId = null,
            Action = "auth.login.failed",
            ResourceType = "Auth",
            Result = AuditResult.Failure,
        };

        await _logger.LogAsync(entry, CancellationToken.None);

        using var context = _factory.CreateDbContext();
        var stored = Assert.Single(context.AuditLogs.ToList());
        Assert.Null(stored.TenantId);
        Assert.Null(stored.ActorUserId);
        Assert.Equal(AuditResult.Failure, stored.Result);
    }

    [Fact]
    public async Task LogAsync_IsAppendOnly_PreservesEarlierRecords()
    {
        var first = new AuditEntry
        {
            ActorUserId = Actor,
            TenantId = TenantA,
            Action = "user.create",
            ResourceType = "User",
            ResourceId = "first",
            Result = AuditResult.Success,
        };
        var second = new AuditEntry
        {
            ActorUserId = Actor,
            TenantId = TenantA,
            Action = "user.deactivate",
            ResourceType = "User",
            ResourceId = "second",
            Result = AuditResult.Success,
        };

        await _logger.LogAsync(first, CancellationToken.None);

        using (var afterFirst = _factory.CreateDbContext())
        {
            var only = Assert.Single(afterFirst.AuditLogs.ToList());
            Assert.Equal("first", only.ResourceId);
        }

        await _logger.LogAsync(second, CancellationToken.None);

        using var context = _factory.CreateDbContext();
        // Ordena no cliente: o SQLite (provider de teste) não traduz ORDER BY
        // sobre DateTimeOffset; no PostgreSQL de produção a ordenação é nativa.
        var all = context.AuditLogs.AsEnumerable().OrderBy(a => a.ResourceId).ToList();

        Assert.Equal(2, all.Count);
        // O primeiro registro permanece inalterado após a segunda gravação (R10.4).
        Assert.Contains(all, a => a.ResourceId == "first" && a.Action == "user.create");
        Assert.Contains(all, a => a.ResourceId == "second" && a.Action == "user.deactivate");
    }
}
