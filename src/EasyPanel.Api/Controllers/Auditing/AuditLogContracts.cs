using EasyPanel.Modules.Auditing;

namespace EasyPanel.Api.Controllers.Auditing;

/// <summary>
/// Projeção segura de um <see cref="AuditLog"/> para a API de consulta (R10.5,
/// R12.1). DTO distinto da entidade de persistência: expõe apenas os campos
/// seguros da trilha.
///
/// <para>
/// Os campos <see cref="OldValues"/>/<see cref="NewValues"/> carregam JSON que já
/// foi <b>redigido na origem</b> (via <see cref="AuditValueRedactor"/>) no momento
/// da gravação — segredos e credenciais nunca foram persistidos (R11.2), de modo
/// que a projeção não precisa remascarar nada.
/// </para>
/// </summary>
/// <param name="Id">Identificador do registro de auditoria.</param>
/// <param name="ActorUserId">Ator que originou o evento, quando conhecido.</param>
/// <param name="Action">Ação executada, no formato <c>recurso.acao</c>.</param>
/// <param name="ResourceType">Tipo do recurso afetado.</param>
/// <param name="ResourceId">Identificador do recurso afetado, quando aplicável.</param>
/// <param name="Result">Resultado do evento (sucesso, recusado ou falha).</param>
/// <param name="OccurredAt">Momento em que o evento ocorreu (UTC).</param>
/// <param name="Ip">Endereço IP de origem, quando disponível.</param>
/// <param name="UserAgent">User-Agent de origem, quando disponível.</param>
/// <param name="OldValues">Estado anterior (JSON já redigido), quando aplicável.</param>
/// <param name="NewValues">Estado novo (JSON já redigido), quando aplicável.</param>
public sealed record AuditLogDto(
    Guid Id,
    Guid? ActorUserId,
    string Action,
    string ResourceType,
    string? ResourceId,
    AuditResult Result,
    DateTimeOffset OccurredAt,
    string? Ip,
    string? UserAgent,
    string? OldValues,
    string? NewValues);

/// <summary>
/// Página de resultados da consulta de auditoria (R10.5/R12.4). Espelha um
/// <see cref="EasyPanel.Shared.Kernel.Pagination.PagedResult{T}"/> de
/// <see cref="AuditLogDto"/> em um contrato de API estável.
/// </summary>
/// <param name="Items">Registros de auditoria da página atual.</param>
/// <param name="Page">Número da página (base 1).</param>
/// <param name="PageSize">Tamanho de página efetivamente aplicado (≤ 100 — R12.4).</param>
/// <param name="TotalCount">Total de registros do tenant, ignorando a paginação.</param>
public sealed record AuditLogPageResponse(
    IReadOnlyList<AuditLogDto> Items,
    int Page,
    int PageSize,
    long TotalCount);
