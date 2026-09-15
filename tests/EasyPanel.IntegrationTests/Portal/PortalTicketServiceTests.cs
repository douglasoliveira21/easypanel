using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Portal;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Portal;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.IntegrationTests.Portal;

/// <summary>
/// Testes do <see cref="PortalTicketService"/> (Fase 10 — R4): listagem e
/// detalhe de Chamados restritos ao Cliente do <see cref="ICustomerContext"/>,
/// isolamento cruzado de Cliente e de tenant.
/// </summary>
public sealed class PortalTicketServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid CustomerA1 = Guid.Parse("c1c1c1c1-1111-1111-1111-c1c1c1c1c1c1");
    private static readonly Guid CustomerA2 = Guid.Parse("c2c2c2c2-2222-2222-2222-c2c2c2c2c2c2");
    private static readonly Guid RequesterUser = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly MutableCustomerContext _customerContext = new();
    private Guid _ticketA1Id;
    private Guid _ticketA2Id;

    public PortalTicketServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _tenantContext.SetSuperAdmin();
        using var seed = CreateContext();
        seed.Database.EnsureCreated();
        SeedTickets();
        _tenantContext.SetTenant(TenantA);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task List_ReturnsOnlyOwnCustomerTickets()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.ListAsync(cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(_ticketA1Id, item.Id);
    }

    [Fact]
    public async Task Get_ForTicketOfAnotherCustomer_ReturnsNotFound()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.GetAsync(_ticketA2Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PortalErrors.NotFound.Code, result.Error.Code);
    }

    [Fact]
    public async Task Get_ForOwnTicket_ReturnsCommentsAndAttachments_ExcludingStatusChanges()
    {
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.GetAsync(_ticketA1Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var comment = Assert.Single(result.Value.Comments);
        Assert.Equal("Comentário visível ao cliente", comment.Comment);
        var attachment = Assert.Single(result.Value.Attachments);
        Assert.Equal("evidencia.png", attachment.FileName);
    }

    [Fact]
    public async Task List_CrossTenant_ReturnsEmpty()
    {
        _tenantContext.SetTenant(Guid.NewGuid());
        _customerContext.SetCustomer(CustomerA1);
        var service = CreateService();

        var result = await service.ListAsync(cursor: null, pageSize: 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    private PortalTicketService CreateService() => new(CreateContext(), _customerContext);

    private void SeedTickets()
    {
        using var context = CreateContext();
        var now = DateTimeOffset.UtcNow;

        context.Set<Customer>().AddRange(
            new Customer { Id = CustomerA1, TenantId = TenantA, RazaoSocial = "Cliente A1", Cnpj = "11222333000181", CreatedAt = now },
            new Customer { Id = CustomerA2, TenantId = TenantA, RazaoSocial = "Cliente A2", Cnpj = "22333444000192", CreatedAt = now });

        _ticketA1Id = Guid.NewGuid();
        _ticketA2Id = Guid.NewGuid();

        context.Set<Ticket>().AddRange(
            new Ticket
            {
                Id = _ticketA1Id, TenantId = TenantA, Title = "Impressora travando", CustomerId = CustomerA1,
                RequestedByUserId = RequesterUser, Status = TicketStatus.EmAndamento,
                CreatedAt = now, CreatedAtTicks = now.UtcTicks,
            },
            new Ticket
            {
                Id = _ticketA2Id, TenantId = TenantA, Title = "Troca de toner", CustomerId = CustomerA2,
                RequestedByUserId = RequesterUser, Status = TicketStatus.Aberto,
                CreatedAt = now, CreatedAtTicks = now.UtcTicks,
            });

        context.Set<TicketInteraction>().AddRange(
            new TicketInteraction
            {
                Id = Guid.NewGuid(), TenantId = TenantA, TicketId = _ticketA1Id,
                Type = TicketInteractionType.Comentario, ActorUserId = RequesterUser,
                Comment = "Comentário visível ao cliente", OccurredAt = now, OccurredAtTicks = now.UtcTicks,
                CreatedAt = now,
            },
            new TicketInteraction
            {
                Id = Guid.NewGuid(), TenantId = TenantA, TicketId = _ticketA1Id,
                Type = TicketInteractionType.MudancaStatus, ActorUserId = RequesterUser,
                FromStatus = TicketStatus.Aberto, ToStatus = TicketStatus.EmAndamento,
                OccurredAt = now, OccurredAtTicks = now.UtcTicks, CreatedAt = now,
            });

        context.Set<TicketAttachment>().Add(new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            TicketId = _ticketA1Id,
            FileName = "evidencia.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"tickets/{TenantA:N}/{_ticketA1Id:N}/evidencia.png",
            UploadedByUserId = RequesterUser,
            UploadedAt = now,
            UploadedAtTicks = now.UtcTicks,
            CreatedAt = now,
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

    private sealed class MutableCustomerContext : ICustomerContext
    {
        public Guid? CustomerId { get; private set; }

        public bool HasCustomer => CustomerId.HasValue;

        public void SetCustomer(Guid customerId) => CustomerId = customerId;
    }
}
