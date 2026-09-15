using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Auditing;

/// <summary>
/// Registro imutável de um evento sensível da plataforma (R10.1): quem fez, o quê,
/// quando e em qual contexto. É a materialização do AuditLog descrito no design.
///
/// <para>
/// <b>Decisão de projeto — herança e <c>TenantId</c>:</b> diferentemente das demais
/// entidades de negócio, <see cref="AuditLog"/> herda de <see cref="BaseEntity"/> e
/// declara um <see cref="TenantId"/> <b>anulável explícito</b>, em vez de herdar de
/// <c>TenantEntity</c>. A razão é dupla:
/// </para>
/// <list type="number">
///   <item>
///     Eventos auditáveis ocorrem fora de um contexto de tenant resolvido — por
///     exemplo, falhas de login anônimas (R10.2) e tentativas de acesso
///     cross-tenant recusadas (R6.9). Como <c>TenantEntity</c> aciona o interceptor
///     de escrita do <c>AppDbContext</c> (que lança <c>CrossTenantAccessException</c>
///     quando não há tenant no contexto), gravar tais eventos como
///     <c>TenantEntity</c> quebraria a auditoria justamente nos casos em que ela é
///     mais necessária. Por isso o <c>TenantId</c> é anulável e a auditoria não
///     participa do interceptor de tenant.
///   </item>
///   <item>
///     A restrição de leitura por tenant (R10.5) é aplicada <b>explicitamente</b> na
///     consulta paginada da tarefa 5.2 (<c>WHERE TenantId == tenantAtual</c>), e não
///     por um filtro global de ORM — mantendo o controle total sobre eventos de
///     plataforma sem tenant.
///   </item>
/// </list>
///
/// <para>
/// <b>Somente-adição (R10.4):</b> por convenção de negócio, nenhum registro de
/// AuditLog é atualizado ou removido. O <c>IAuditLogger</c> expõe apenas inserção;
/// não há API de mutação em lugar algum.
/// </para>
/// </summary>
public sealed class AuditLog : BaseEntity
{
    /// <summary>
    /// Identificador do ator que originou o evento, quando conhecido. Anulável para
    /// eventos sem ator identificado (ex.: falha de login com email inexistente).
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Tenant no qual a ação ocorreu (R10.3). Anulável para eventos de plataforma
    /// sem tenant resolvido (ex.: falha de login anônima). Ver decisão de projeto
    /// na documentação da classe.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Ação executada, no formato <c>recurso.acao</c> (ex.: "user.create", "auth.login.failed").</summary>
    public required string Action { get; set; }

    /// <summary>Tipo do recurso afetado (ex.: "User", "Customer", "Auth").</summary>
    public required string ResourceType { get; set; }

    /// <summary>Identificador do recurso afetado, quando aplicável.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Valores anteriores (JSON, com redaction), quando aplicável.</summary>
    public string? OldValues { get; set; }

    /// <summary>Valores novos (JSON, com redaction), quando aplicável.</summary>
    public string? NewValues { get; set; }

    /// <summary>Endereço IP de origem, quando disponível.</summary>
    public string? Ip { get; set; }

    /// <summary>User-Agent de origem, quando disponível.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Resultado do evento (sucesso, recusado ou falha).</summary>
    public AuditResult Result { get; set; }

    /// <summary>Momento em que o evento ocorreu (UTC).</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
