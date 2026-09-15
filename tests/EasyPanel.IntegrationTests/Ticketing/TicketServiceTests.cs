using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Infrastructure.Ticketing;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyPanel.IntegrationTests.Ticketing;

/// <summary>
/// Testes do <see cref="TicketService"/> (Task 3.2 — R1-R4): abertura com
/// cálculo de prazos de SLA, transições de status válidas/inválidas, marcação
/// de cumprimento de SLA, atribuição, comentário/primeira resposta, cursor
/// pagination do histórico, consulta de SLA violado, isolamento cross-tenant.
/// </summary>
public sealed class TicketServiceTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a1a1a1a1-1111-1111-1111-a1a1a1a1a1a1");
    private static readonly Guid TenantB = Guid.Parse("b2b2b2b2-2222-2222-2222-b2b2b2b2b2b2");
    private static readonly Guid CustomerA = Guid.Parse("c3c3c3c3-3333-3333-3333-c3c3c3c3c3c3");
    private static readonly Guid RequesterId = Guid.Parse("d4d4d4d4-4444-4444-4444-d4d4d4d4d4d4");
    private static readonly Guid TechnicianId = Guid.Parse("e5e5e5e5-5555-5555-5555-e5e5e5e5e5e5");
    private static readonly Guid OtherTenantUserId = Guid.Parse("f6f6f6f6-6666-6666-6666-f6f6f6f6f6f6");

    private readonly SqliteConnection _connection;
    private readonly MutableTenantContext _tenantContext = new();
    private readonly FakeAuditLogger _audit = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));
    private readonly IOptions<TicketingOptions> _options = Options.Create(new TicketingOptions());

    public TicketServiceTests()
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
    public async Task Create_CalculatesSlaDueDates_FromDefaultOptions()
    {
        var service = CreateService(RequesterId);

        var result = await service.CreateAsync(
            new CreateTicketRequest("Impressora não liga", null, CustomerA, null, null, TicketPriority.Alta),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ticket = result.Value;
        var expectedFirstResponse = ticket.CreatedAt.AddMinutes(_options.Value.Alta.FirstResponseMinutes);
        var expectedResolution = ticket.CreatedAt.AddMinutes(_options.Value.Alta.ResolutionMinutes);
        Assert.Equal(expectedFirstResponse, ticket.FirstResponseDueAt);
        Assert.Equal(expectedResolution, ticket.ResolutionDueAt);
        Assert.Equal(TicketStatus.Aberto, ticket.Status);
        Assert.Contains(_audit.Entries, e => e.Action == "ticket.create");
    }

    [Fact]
    public async Task Create_UsesConfiguredSlaPolicy_WhenPresent()
    {
        var slaService = new SlaPolicyService(CreateContext(), new FakeCurrentUser(TechnicianId), _audit, _clock);
        await slaService.SetAsync(new SetSlaPolicyRequest(TicketPriority.Urgente, 15, 60), CancellationToken.None);

        var service = CreateService(RequesterId);
        var result = await service.CreateAsync(
            new CreateTicketRequest("Fogo!", null, CustomerA, null, null, TicketPriority.Urgente),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var ticket = result.Value;
        Assert.Equal(ticket.CreatedAt.AddMinutes(15), ticket.FirstResponseDueAt);
        Assert.Equal(ticket.CreatedAt.AddMinutes(60), ticket.ResolutionDueAt);
    }

    [Fact]
    public async Task Create_MissingTitle_Fails()
    {
        var service = CreateService(RequesterId);

        var result = await service.CreateAsync(
            new CreateTicketRequest(" ", null, CustomerA, null, null, TicketPriority.Media),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.TitleRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_InvalidCustomer_Fails()
    {
        var service = CreateService(RequesterId);

        var result = await service.CreateAsync(
            new CreateTicketRequest("Chamado", null, Guid.NewGuid(), null, null, TicketPriority.Media),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.InvalidCustomer.Code, result.Error.Code);
    }

    [Fact]
    public async Task ChangeStatus_ValidTransition_RecordsInteraction()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        var result = await service.ChangeStatusAsync(
            ticket.Id, new ChangeTicketStatusRequest(TicketStatus.EmAndamento), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TicketStatus.EmAndamento, result.Value.Status);
        Assert.Contains(_audit.Entries, e => e.Action == "ticket.status_change");

        var interactions = await service.ListInteractionsAsync(ticket.Id, null, 10, CancellationToken.None);
        Assert.Contains(interactions.Value.Items, i => i.Type == TicketInteractionType.MudancaStatus
            && i.FromStatus == TicketStatus.Aberto && i.ToStatus == TicketStatus.EmAndamento);
    }

    [Fact]
    public async Task ChangeStatus_InvalidTransition_Fails()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        // Aberto -> Resolvido não é uma transição permitida.
        var result = await service.ChangeStatusAsync(
            ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Resolvido), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.InvalidStatusTransition.Code, result.Error.Code);
    }

    [Fact]
    public async Task ChangeStatus_ToResolvido_MarksResolutionCompliance()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service, TicketPriority.Urgente);
        await service.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.EmAndamento), CancellationToken.None);

        var result = await service.ChangeStatusAsync(
            ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Resolvido), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ResolvedAt);
        Assert.Equal(SlaComplianceStatus.Cumprido, result.Value.ResolutionCompliance);
    }

    [Fact]
    public async Task Assign_ValidUser_RecordsInteraction()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        var result = await service.AssignAsync(ticket.Id, new AssignTicketRequest(TechnicianId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TechnicianId, result.Value.AssignedToUserId);
        Assert.Contains(_audit.Entries, e => e.Action == "ticket.assign");
    }

    [Fact]
    public async Task Assign_UserFromAnotherTenant_Fails()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        var result = await service.AssignAsync(ticket.Id, new AssignTicketRequest(OtherTenantUserId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.InvalidAssignee.Code, result.Error.Code);
    }

    [Fact]
    public async Task AddComment_FromTechnician_MarksFirstResponseCompliance()
    {
        var requesterService = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(requesterService);

        var technicianService = CreateService(TechnicianId);
        var result = await technicianService.AddCommentAsync(
            ticket.Id, new AddTicketCommentRequest("Já estou verificando."), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = await requesterService.GetAsync(ticket.Id, CancellationToken.None);
        Assert.NotNull(updated.Value.FirstResponseAt);
        Assert.Equal(SlaComplianceStatus.Cumprido, updated.Value.FirstResponseCompliance);
    }

    [Fact]
    public async Task AddComment_FromRequester_DoesNotMarkFirstResponse()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        await service.AddCommentAsync(ticket.Id, new AddTicketCommentRequest("Complementando o chamado."), CancellationToken.None);

        var updated = await service.GetAsync(ticket.Id, CancellationToken.None);
        Assert.Null(updated.Value.FirstResponseAt);
        Assert.Equal(SlaComplianceStatus.Pendente, updated.Value.FirstResponseCompliance);
    }

    [Fact]
    public async Task AddComment_Empty_Fails()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        var result = await service.AddCommentAsync(ticket.Id, new AddTicketCommentRequest("  "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.CommentRequired.Code, result.Error.Code);
    }

    [Fact]
    public async Task ListInteractions_CursorPagination_ReturnsPagesWithoutOverlap()
    {
        var service = CreateService(TechnicianId);
        var ticket = await CreateTicketAsync(service);
        for (var i = 0; i < 5; i++)
        {
            await service.AddCommentAsync(ticket.Id, new AddTicketCommentRequest($"Comentário {i}"), CancellationToken.None);
        }

        var firstPage = await service.ListInteractionsAsync(ticket.Id, null, 2, CancellationToken.None);
        Assert.True(firstPage.IsSuccess);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.NotNull(firstPage.Value.NextCursor);

        var secondPage = await service.ListInteractionsAsync(ticket.Id, firstPage.Value.NextCursor, 2, CancellationToken.None);
        Assert.Equal(2, secondPage.Value.Items.Count);

        var firstIds = firstPage.Value.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(secondPage.Value.Items, i => firstIds.Contains(i.Id));
    }

    [Fact]
    public async Task ListSlaBreached_ReturnsOverdueOpenTicket()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service, TicketPriority.Urgente);

        // Avança o relógio além do prazo de primeira resposta/resolução sem responder.
        _clock.Advance(TimeSpan.FromHours(6));

        var breached = await service.ListSlaBreachedAsync(new PageRequest(1, 10), CancellationToken.None);

        Assert.True(breached.IsSuccess);
        Assert.Contains(breached.Value.Items, t => t.Id == ticket.Id);
    }

    [Fact]
    public async Task ListSlaBreached_ExcludesCancelledTicket()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service, TicketPriority.Urgente);
        await service.ChangeStatusAsync(ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Cancelado), CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(6));

        var breached = await service.ListSlaBreachedAsync(new PageRequest(1, 10), CancellationToken.None);

        Assert.DoesNotContain(breached.Value.Items, t => t.Id == ticket.Id);
    }

    [Fact]
    public async Task Get_CrossTenant_NotFound()
    {
        var service = CreateService(RequesterId);
        var ticket = await CreateTicketAsync(service);

        _tenantContext.SetTenant(TenantB);
        var serviceB = CreateService(RequesterId);

        var result = await serviceB.GetAsync(ticket.Id, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TicketingErrors.NotFound.Code, result.Error.Code);
    }

    private async Task<TicketDto> CreateTicketAsync(ITicketService service, TicketPriority priority = TicketPriority.Media)
    {
        var result = await service.CreateAsync(
            new CreateTicketRequest("Chamado de teste", "Descrição", CustomerA, null, null, priority),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private TicketService CreateService(Guid actorId) =>
        new(CreateContext(), _tenantContext, new FakeCurrentUser(actorId), _audit, _clock, _options);

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

        context.Users.Add(new ApplicationUser
        {
            Id = TechnicianId,
            TenantId = TenantA,
            UserName = $"{TenantA:D}:tecnico@teste.com",
            Email = "tecnico@teste.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        context.Users.Add(new ApplicationUser
        {
            Id = OtherTenantUserId,
            TenantId = TenantB,
            UserName = $"{TenantB:D}:outro@teste.com",
            Email = "outro@teste.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        context.SaveChanges();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _tenantContext);
    }

    private sealed class FakeCurrentUser(Guid userId) : ICurrentUserAccessor
    {
        public Guid? UserId => userId;
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

    /// <summary>
    /// Avança 1 tick a cada chamada — evita que interações registradas em
    /// sequência rápida no mesmo teste colidam em <c>OccurredAtTicks</c>. Também
    /// permite avançar manualmente (<see cref="Advance"/>) para simular a
    /// passagem do tempo além de um prazo de SLA.
    /// </summary>
    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _current = start;

        public override DateTimeOffset GetUtcNow() => _current = _current.AddTicks(1);

        public void Advance(TimeSpan delta) => _current = _current.Add(delta);
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
