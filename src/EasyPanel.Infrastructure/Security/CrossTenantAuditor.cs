using EasyPanel.Modules.Auditing;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ICrossTenantAuditor"/> sobre o
/// <see cref="IAuditLogger"/> (R6.9).
///
/// <para>
/// Traduz uma recusa de acesso cross-tenant em um <see cref="AuditEntry"/> com
/// <see cref="AuditResult.Denied"/> e a ação
/// <see cref="ICrossTenantAuditor.CrossTenantDeniedAction"/>, delegando a
/// persistência ao logger. Como o <see cref="AuditLogger"/> grava por um contexto
/// dedicado e isolado (não participa do interceptor de tenant), o registro é
/// atômico mesmo quando a transação de negócio que originou a recusa é revertida.
/// </para>
///
/// <para>
/// Este componente é o ponto de extensão que o middleware global de erros (tarefa
/// 9.1) e os serviços de negócio (tarefas 6–8) invocam ao capturar a recusa,
/// evitando injetar o <see cref="IAuditLogger"/> diretamente no
/// <c>AppDbContext</c> (que criaria um ciclo de dependências).
/// </para>
/// </summary>
public sealed class CrossTenantAuditor : ICrossTenantAuditor
{
    private readonly IAuditLogger _auditLogger;

    /// <summary>Cria o auditor de recusas com o logger de auditoria subjacente.</summary>
    public CrossTenantAuditor(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
    }

    /// <inheritdoc />
    public Task RecordDeniedAsync(
        Guid? actorUserId,
        Guid? tenantId,
        string resourceType,
        string? resourceId,
        string? ip,
        string? userAgent,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var entry = new AuditEntry
        {
            ActorUserId = actorUserId,
            TenantId = tenantId,
            Action = ICrossTenantAuditor.CrossTenantDeniedAction,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Ip = ip,
            UserAgent = userAgent,
            Result = AuditResult.Denied,
        };

        return _auditLogger.LogAsync(entry, ct);
    }
}
