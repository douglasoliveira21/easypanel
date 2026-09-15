using System.Linq.Expressions;
using System.Reflection;
using EasyPanel.Modules.Alerting;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Billing;
using EasyPanel.Modules.Contracts;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Inventory;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Entities;
using EasyPanel.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Contexto de persistência principal da plataforma sobre PostgreSQL (Npgsql).
///
/// Convenções aplicadas de forma centralizada (R1, design "Estratégia de tipos"):
/// <list type="bullet">
///   <item>Chaves primárias <see cref="Guid"/> em toda <see cref="BaseEntity"/>.</item>
///   <item>Carimbos temporais como <see cref="DateTimeOffset"/> armazenados em UTC.</item>
///   <item>Enums persistidos como <c>int</c>.</item>
/// </list>
///
/// Isolamento multi-tenant (R6): toda entidade que herda de
/// <see cref="TenantEntity"/> recebe automaticamente um filtro global de consulta
/// por <c>TenantId</c> (R6.4) e tem sua escrita validada por um interceptor de
/// <c>SaveChanges</c> (R6.5, R6.6). O tenant corrente é lido do
/// <see cref="ITenantContext"/> injetado, capturado em campo para que o filtro
/// seja reavaliado por consulta.
///
/// Configurações específicas de entidade (índices, filtros de tenant, etc.) são
/// adicionadas por classes <see cref="IEntityTypeConfiguration{TEntity}"/> à medida
/// que as entidades são introduzidas nas tarefas subsequentes.
///
/// Autenticação (R2/R3/R4): deriva de
/// <see cref="IdentityDbContext{TUser,TRole,TKey}"/> para materializar as tabelas do
/// ASP.NET Core Identity (usuários, papéis, claims, logins, tokens) sobre PostgreSQL.
/// <c>base.OnModelCreating</c> é invocado primeiro, mapeando o Identity, antes das
/// convenções e filtros da plataforma. <see cref="ApplicationUser"/> tem
/// <c>TenantId</c> anulável e não herda de <see cref="TenantEntity"/>, portanto não
/// recebe filtro global de tenant nem a convenção de PK Guid — o Identity gera as
/// chaves no cliente por padrão.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Cria o contexto com as opções do EF Core e o contexto de tenant da
    /// requisição corrente. Ambos são <c>scoped</c>, de modo que cada requisição
    /// possui seu próprio <see cref="AppDbContext"/> e <see cref="ITenantContext"/>.
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>Tenants: unidade de isolamento de dados de negócio (R6.1).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Refresh tokens opacos e rotativos dos usuários (R2.1, R2.4, R2.5).</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Clientes do tenant (R8). <see cref="Customer"/> é uma <c>TenantEntity</c>,
    /// portanto recebe o filtro global de consulta por <c>TenantId</c> (R8.7/R8.8)
    /// e a validação de escrita do interceptor de <c>SaveChanges</c> (R6/R8).
    /// </summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>
    /// Locais (pontos de instalação) do tenant (R9). <see cref="Location"/> é uma
    /// <c>TenantEntity</c>, portanto recebe o filtro global de consulta por
    /// <c>TenantId</c> (R9.6/R9.7) e a validação de escrita do interceptor de
    /// <c>SaveChanges</c> (R6/R9). Cada local pertence a um <see cref="Customer"/>.
    /// </summary>
    public DbSet<Location> Locations => Set<Location>();

    /// <summary>
    /// Trilha de auditoria somente-adição (R10). <see cref="AuditLog"/> herda de
    /// <c>BaseEntity</c> com <c>TenantId</c> anulável (não é <c>TenantEntity</c>),
    /// portanto não recebe filtro global de tenant nem participa do interceptor de
    /// escrita — permitindo registrar eventos de plataforma sem tenant (ex.: falha
    /// de login anônima, R10.2). A restrição de leitura por tenant (R10.5) é
    /// aplicada explicitamente na consulta.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Agentes Windows registrados (R3). <see cref="WindowsClient"/> é uma
    /// <c>TenantEntity</c>, recebendo o filtro global por <c>TenantId</c> e a
    /// validação de escrita do interceptor de <c>SaveChanges</c>.
    /// </summary>
    public DbSet<WindowsClient> WindowsClients => Set<WindowsClient>();

    /// <summary>Parque de impressoras monitoradas (R9), isolado por tenant.</summary>
    public DbSet<Printer> Printers => Set<Printer>();

    /// <summary>Leituras de contadores somente-adição (R11), isoladas por tenant.</summary>
    public DbSet<PrinterCounter> PrinterCounters => Set<PrinterCounter>();

    /// <summary>Eventos de impressora/agente somente-adição (R6.4/R12.3).</summary>
    public DbSet<PrinterEvent> PrinterEvents => Set<PrinterEvent>();

    /// <summary>Coletas executadas pelos agentes (R12), com idempotência (R13).</summary>
    public DbSet<Collection> Collections => Set<Collection>();

    /// <summary>Histórico de movimentação/ciclo de vida de impressoras (R10).</summary>
    public DbSet<PrinterMovement> PrinterMovements => Set<PrinterMovement>();

    /// <summary>Deduplicação de ingestão por chave de idempotência (R13).</summary>
    public DbSet<IngestionDedup> IngestionDedups => Set<IngestionDedup>();

    /// <summary>Refresh tokens opacos e rotativos dos agentes Windows (R4.4).</summary>
    public DbSet<ClientRefreshToken> ClientRefreshTokens => Set<ClientRefreshToken>();

    /// <summary>Chaves de provisionamento de Local para registro de agentes (R3.2).</summary>
    public DbSet<LocationProvisioningKey> LocationProvisioningKeys => Set<LocationProvisioningKey>();

    /// <summary>Leituras de nível de suprimento somente-adição (Fase 4), isoladas por tenant.</summary>
    public DbSet<SupplyReading> SupplyReadings => Set<SupplyReading>();

    /// <summary>Limiares configuráveis de suprimento (Fase 4), isolados por tenant.</summary>
    public DbSet<SupplyThreshold> SupplyThresholds => Set<SupplyThreshold>();

    /// <summary>Regras de alerta configuradas por tenant (Fase 3 — R1), isoladas por tenant.</summary>
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();

    /// <summary>Alertas gerados pelo Motor_de_Alertas (Fase 3 — R2/R3), isolados por tenant.</summary>
    public DbSet<Alert> Alerts => Set<Alert>();

    /// <summary>Histórico somente-adição de transições de estado de Alerta (Fase 3 — R3.5).</summary>
    public DbSet<AlertTransition> AlertTransitions => Set<AlertTransition>();

    /// <summary>Fila de despacho de notificação de Alerta (Fase 3 — R4/R5).</summary>
    public DbSet<AlertNotificationOutbox> AlertNotificationOutbox => Set<AlertNotificationOutbox>();

    /// <summary>Histórico somente-adição de tentativas de notificação (Fase 3 — R6).</summary>
    public DbSet<AlertNotificationAttempt> AlertNotificationAttempts => Set<AlertNotificationAttempt>();

    /// <summary>Silenciamentos de alerta por regra e/ou impressora/agente (Fase 3 — R7).</summary>
    public DbSet<AlertSilence> AlertSilences => Set<AlertSilence>();

    /// <summary>
    /// Cursor único de plataforma do Motor_de_Alertas (Fase 3 — R2.1/R2.8). Não é
    /// uma <c>TenantEntity</c> (mesmo padrão de <see cref="AuditLog"/>).
    /// </summary>
    public DbSet<AlertEngineCheckpoint> AlertEngineCheckpoints => Set<AlertEngineCheckpoint>();

    /// <summary>Catálogo de itens de estoque (Fase 5 — R1), isolado por tenant.</summary>
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    /// <summary>Movimentações de estoque somente-adição (Fase 5 — R2), isoladas por tenant.</summary>
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    /// <summary>Saldo materializado por (Item, Local) (Fase 5 — R3), isolado por tenant.</summary>
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();

    /// <summary>Estoque mínimo configurável por (Item, Local) (Fase 5 — R5), isolado por tenant.</summary>
    public DbSet<InventoryMinimum> InventoryMinimums => Set<InventoryMinimum>();

    /// <summary>Chamados abertos pelo/para o tenant (Fase 6 — R1/R2/R4), isolados por tenant.</summary>
    public DbSet<Ticket> Tickets => Set<Ticket>();

    /// <summary>Histórico somente-adição de interações de um Chamado (Fase 6 — R2.5/R2.6), isolado por tenant.</summary>
    public DbSet<TicketInteraction> TicketInteractions => Set<TicketInteraction>();

    /// <summary>Política de SLA por (tenant, prioridade) (Fase 6 — R4), isolada por tenant.</summary>
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();

    /// <summary>Metadados de anexos de Chamado (Fase 6 — R5), isolados por tenant.</summary>
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    /// <summary>Contratos comerciais de Cliente, com vigência e ciclo de vida (Fase 7 — R1/R5), isolados por tenant.</summary>
    public DbSet<Contract> Contracts => Set<Contract>();

    /// <summary>Escopo de Contrato por Local (Fase 7 — R2), isolado por tenant.</summary>
    public DbSet<ContractLocation> ContractLocations => Set<ContractLocation>();

    /// <summary>Escopo de Contrato por Impressora (Fase 7 — R2), isolado por tenant.</summary>
    public DbSet<ContractPrinter> ContractPrinters => Set<ContractPrinter>();

    /// <summary>Franquia por tipo de contador de um Contrato (Fase 7 — R3), isolada por tenant.</summary>
    public DbSet<ContractFranchise> ContractFranchises => Set<ContractFranchise>();

    /// <summary>Registro somente-adição de execução de fechamento mensal (Fase 8 — R1), isolado por tenant.</summary>
    public DbSet<BillingClosing> BillingClosings => Set<BillingClosing>();

    /// <summary>Faturas geradas por fechamento (Fase 8 — R4/R5), isoladas por tenant.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>Itens de excedente de uma Fatura (Fase 8 — R4.3), isolados por tenant.</summary>
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica todas as IEntityTypeConfiguration<> declaradas neste assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ApplyConventions(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // DateTimeOffset em UTC: garante comparação/armazenamento consistentes.
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOnWrite();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnforceTenantOnWrite();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Aplica o filtro global de consulta por <c>TenantId</c> a toda entidade que
    /// herda de <see cref="TenantEntity"/> (R6.4).
    ///
    /// O predicado referencia o campo <see cref="_tenantContext"/> (instância),
    /// de modo que o tenant corrente é reavaliado a cada consulta. Um Super Admin
    /// operando fora de um tenant específico (TenantId nulo) não é restringido,
    /// permitindo leituras administrativas cross-tenant nos endpoints designados
    /// (R6.7). Um usuário comum sem tenant não enxerga nenhuma linha.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var method = typeof(AppDbContext)
                .GetMethod(nameof(BuildTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType);

            var filter = (LambdaExpression)method.Invoke(this, parameters: null)!;
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    /// <summary>
    /// Constrói o predicado de filtro por tenant para <typeparamref name="TEntity"/>.
    /// Fecha sobre <c>this</c> para reavaliar o tenant corrente por consulta.
    /// </summary>
    private Expression<Func<TEntity, bool>> BuildTenantFilter<TEntity>()
        where TEntity : TenantEntity
        => e => _tenantContext.IsSuperAdmin
            || (_tenantContext.TenantId != null && e.TenantId == _tenantContext.TenantId);

    /// <summary>
    /// Interceptor de escrita (R6.5, R6.6): antes de persistir, percorre as
    /// entradas <see cref="TenantEntity"/> rastreadas e:
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="EntityState.Added"/>: preenche o <c>TenantId</c> a partir do
    ///     contexto quando ausente; se o contexto tem tenant e a entidade já traz
    ///     um <c>TenantId</c> divergente e não vazio, trata como escrita
    ///     cross-tenant e lança <see cref="CrossTenantAccessException"/> (R6.6). Sem
    ///     tenant no contexto (e não Super Admin) para uma entidade sem tenant, é
    ///     erro — não há tenant para persistir o dado.
    ///   </item>
    ///   <item>
    ///     <see cref="EntityState.Modified"/> / <see cref="EntityState.Deleted"/>:
    ///     se o <c>TenantId</c> da entidade diverge do contexto (e não Super Admin),
    ///     lança <see cref="CrossTenantAccessException"/> (R6.5).
    ///   </item>
    /// </list>
    /// A entidade <see cref="Tenant"/> não é <see cref="TenantEntity"/> e, portanto,
    /// não é submetida a estas regras.
    /// </summary>
    private void EnforceTenantOnWrite()
    {
        var currentTenantId = _tenantContext.TenantId;
        var isSuperAdmin = _tenantContext.IsSuperAdmin;

        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    HandleAdded(entry, currentTenantId, isSuperAdmin);
                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    HandleModifiedOrDeleted(entry, currentTenantId, isSuperAdmin);
                    break;

                default:
                    break;
            }
        }
    }

    private static void HandleAdded(
        EntityEntry<TenantEntity> entry,
        Guid? currentTenantId,
        bool isSuperAdmin)
    {
        var entityTenantId = entry.Entity.TenantId;

        // Super Admin pode escrever explicitamente em qualquer tenant, desde que
        // a entidade traga um TenantId. Quando não traz, herda o tenant alvo do
        // contexto (se houver).
        if (isSuperAdmin)
        {
            if (entityTenantId == Guid.Empty)
            {
                if (currentTenantId is null)
                {
                    // Não há tenant alvo: não é possível persistir dado de tenant.
                    throw new CrossTenantAccessException();
                }

                entry.Entity.TenantId = currentTenantId.Value;
            }

            return;
        }

        if (currentTenantId is null)
        {
            // Usuário sem tenant não pode persistir dado de tenant.
            throw new CrossTenantAccessException();
        }

        if (entityTenantId == Guid.Empty)
        {
            entry.Entity.TenantId = currentTenantId.Value;
            return;
        }

        if (entityTenantId != currentTenantId.Value)
        {
            // Tentativa de gravar dado marcado para outro tenant (R6.6).
            throw new CrossTenantAccessException();
        }
    }

    private static void HandleModifiedOrDeleted(
        EntityEntry<TenantEntity> entry,
        Guid? currentTenantId,
        bool isSuperAdmin)
    {
        if (isSuperAdmin)
        {
            return;
        }

        if (currentTenantId is null || entry.Entity.TenantId != currentTenantId.Value)
        {
            // Alteração/remoção de registro de outro tenant (R6.5) → HTTP 404.
            throw new CrossTenantAccessException();
        }
    }

    /// <summary>
    /// Aplica convenções transversais ao modelo após o carregamento das
    /// configurações de entidade.
    /// </summary>
    private static void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Convenção: toda entidade que herda de BaseEntity usa Guid como PK.
            if (typeof(BaseEntity).IsAssignableFrom(clrType))
            {
                var idProperty = entityType.FindProperty(nameof(BaseEntity.Id));
                if (idProperty is not null)
                {
                    // Guid v7/sequencial é preferido para localidade de índice;
                    // o valor é atribuído pela aplicação (não pelo banco) para
                    // permitir gerar Ids determinísticos onde necessário.
                    idProperty.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
                }
            }

            // Convenção: enums persistidos como int.
            foreach (var property in entityType.GetProperties())
            {
                var propertyType = property.ClrType;
                var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                if (underlying.IsEnum)
                {
                    property.SetProviderClrType(
                        Nullable.GetUnderlyingType(propertyType) is not null
                            ? typeof(int?)
                            : typeof(int));
                }
            }
        }
    }
}
