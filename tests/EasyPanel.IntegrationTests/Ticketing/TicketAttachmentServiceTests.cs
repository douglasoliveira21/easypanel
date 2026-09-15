using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Ticketing;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using EasyPanel.Shared.Kernel.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Ticketing;

/// <summary>
/// Testes do <see cref="TicketAttachmentService"/> (Task 3.4 — R5), sobre um
/// <see cref="IFileStorage"/> fake em memória (sem depender de um MinIO real —
/// ver lacuna de cobertura registrada em <c>HANDOFF.md</c>): upload válido,
/// tamanho acima do máximo, tipo fora da allowlist, download/listagem
/// restritos ao chamado/tenant.
/// </summary>
public sealed class TicketAttachmentServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
    private readonly InMemoryFileStorage _storage = new();
    private readonly IOptions<TicketingOptions> _options = Options.Create(new TicketingOptions());
    private Guid _ticketId;

    public TicketAttachmentServiceTests()
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
    public async Task UploadAsync_ValidFile_PersistsMetadata_AndStoresContent()
    {
        var service = CreateService();
        using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await service.UploadAsync(
            _ticketId, new UploadTicketAttachmentRequest("foto.png", "image/png", content.Length), content, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("foto.png", result.Value.FileName);
        Assert.Single(_storage.Objects);
        Assert.Contains(_audit.Entries, e => e.Action == "ticket.attachment_upload");
    }

    [Fact]
    public async Task UploadAsync_AboveMaxSize_Fails_WithoutCallingStorage()
    {
        var service = CreateService();
        using var content = new MemoryStream([1, 2, 3]);
        var tooLarge = _options.Value.MaxAttachmentSizeBytes + 1;

        var result = await service.UploadAsync(
            _ticketId, new UploadTicketAttachmentRequest("grande.pdf", "application/pdf", tooLarge), content, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.AttachmentTooLarge.Code, result.Error.Code);
        Assert.Empty(_storage.Objects);
    }

    [Fact]
    public async Task UploadAsync_DisallowedContentType_Fails_WithoutCallingStorage()
    {
        var service = CreateService();
        using var content = new MemoryStream([1, 2, 3]);

        var result = await service.UploadAsync(
            _ticketId, new UploadTicketAttachmentRequest("script.exe", "application/x-msdownload", content.Length), content, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.AttachmentTypeNotAllowed.Code, result.Error.Code);
        Assert.Empty(_storage.Objects);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsUploadedContent()
    {
        var service = CreateService();
        using var content = new MemoryStream([9, 8, 7]);
        var uploaded = await service.UploadAsync(
            _ticketId, new UploadTicketAttachmentRequest("doc.pdf", "application/pdf", content.Length), content, CancellationToken.None);

        var download = await service.DownloadAsync(_ticketId, uploaded.Value.Id, CancellationToken.None);

        Assert.True(download.IsSuccess);
        using var reader = new MemoryStream();
        await download.Value.Content.CopyToAsync(reader);
        Assert.Equal([9, 8, 7], reader.ToArray());
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyAttachmentsOfTheTicket()
    {
        var service = CreateService();
        using var content = new MemoryStream([1]);
        await service.UploadAsync(
            _ticketId, new UploadTicketAttachmentRequest("a.png", "image/png", 1), content, CancellationToken.None);

        var list = await service.ListAsync(_ticketId, CancellationToken.None);

        Assert.True(list.IsSuccess);
        Assert.Single(list.Value);
    }

    [Fact]
    public async Task DownloadAsync_CrossTenantTicket_NotFound()
    {
        var service = CreateService();
        using var content = new MemoryStream([1]);
        var uploaded = await service.UploadAsync(
            _ticketId, new UploadTicketAttachmentRequest("a.png", "image/png", 1), content, CancellationToken.None);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService();

        var download = await serviceB.DownloadAsync(_ticketId, uploaded.Value.Id, CancellationToken.None);

        Assert.True(download.IsFailure);
        Assert.Equal(TicketingErrors.NotFound.Code, download.Error.Code);
    }

    private TicketAttachmentService CreateService() =>
        new(CreateContext(), new FakeCurrentUser(), _audit, _clock, _storage, _options);

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

        _ticketId = Guid.NewGuid();
        context.Set<Ticket>().Add(new Ticket
        {
            Id = _ticketId,
            TenantId = TenantA,
            Title = "Chamado de teste",
            CustomerId = CustomerA,
            Priority = TicketPriority.Media,
            Status = TicketStatus.Aberto,
            RequestedByUserId = Guid.NewGuid(),
            FirstResponseDueAt = DateTimeOffset.UtcNow,
            ResolutionDueAt = DateTimeOffset.UtcNow,
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

    /// <summary>
    /// Fake em memória de <see cref="IFileStorage"/> — usado porque este ambiente
    /// de teste não tem um MinIO real disponível (ver Task 3.1/HANDOFF.md). Cobre
    /// o contrato (grava, lê, isola por chave) sem exercitar o SDK MinIO real.
    /// </summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];

        public Task<Result> UploadAsync(string key, Stream content, string contentType, long sizeBytes, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Objects[key] = buffer.ToArray();
            return Task.FromResult(Result.Success());
        }

        public Task<Result<Stream>> DownloadAsync(string key, CancellationToken ct) =>
            Objects.TryGetValue(key, out var bytes)
                ? Task.FromResult(Result.Success<Stream>(new MemoryStream(bytes)))
                : Task.FromResult(Result.Failure<Stream>(FileStorageErrors.NotFound));

        public Task<Result> DeleteAsync(string key, CancellationToken ct)
        {
            Objects.Remove(key);
            return Task.FromResult(Result.Success());
        }
    }
}
