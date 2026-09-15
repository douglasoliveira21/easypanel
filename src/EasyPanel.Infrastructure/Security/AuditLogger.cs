using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IAuditLogger"/> sobre o <see cref="AppDbContext"/>
/// (R10.1, R10.3, R10.4).
///
/// <para>
/// <b>Somente-adição (R10.4):</b> o logger apenas <b>insere</b> registros de
/// <see cref="AuditLog"/>. Não expõe — nem executa — qualquer atualização ou
/// remoção de registros existentes.
/// </para>
///
/// <para>
/// <b>Persistência independente (por que <see cref="IDbContextFactory{TContext}"/>):</b>
/// o <see cref="AppDbContext"/> scoped da requisição pode conter alterações
/// pendentes de outras entidades. Como o <c>SaveChanges</c> do <c>AppDbContext</c>
/// aciona o interceptor de tenant sobre <b>todas</b> as entradas rastreadas, gravar
/// a auditoria pelo contexto scoped acoplaria o registro do evento ao flush de
/// mudanças alheias — problemático justamente em eventos que ocorrem sem tenant
/// resolvido (falha de login, acesso cross-tenant recusado). Por isso o logger
/// cria um <see cref="AppDbContext"/> efêmero e dedicado por chamada, contendo
/// somente o <see cref="AuditLog"/> a ser inserido. Como <see cref="AuditLog"/> não
/// é uma <c>TenantEntity</c>, o interceptor de tenant o ignora, tornando a escrita
/// atômica e isolada.
/// </para>
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Cria o logger com a fábrica de contextos dedicada à auditoria e o provedor
    /// de tempo usado para carimbar <c>OccurredAt</c> (UTC).
    /// </summary>
    public AuditLogger(IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task LogAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAt = _timeProvider.GetUtcNow(),
            CreatedAt = _timeProvider.GetUtcNow(),
            ActorUserId = entry.ActorUserId,
            TenantId = entry.TenantId,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            OldValues = entry.OldValues,
            NewValues = entry.NewValues,
            Ip = entry.Ip,
            UserAgent = entry.UserAgent,
            Result = entry.Result,
        };

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        context.AuditLogs.Add(log);
        await context.SaveChangesAsync(ct);
    }
}
