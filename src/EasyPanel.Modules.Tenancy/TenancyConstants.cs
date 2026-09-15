namespace EasyPanel.Modules.Tenancy;

/// <summary>
/// Constantes bem conhecidas do módulo de Tenancy, compartilhadas entre a
/// resolução de tenant (middleware), a emissão de tokens (Identity) e os testes.
///
/// Centraliza o nome do claim de tenant, o nome do papel de Super Admin e o
/// cabeçalho administrativo designado, de modo que a origem do <c>tenant_id</c>
/// permaneça única e auditável (R6.3, R6.7).
/// </summary>
public static class TenancyConstants
{
    /// <summary>
    /// Nome do claim que carrega o identificador do tenant no
    /// <c>ClaimsPrincipal</c> autenticado. É a única fonte de verdade do tenant
    /// para usuários comuns (R6.3).
    /// </summary>
    public const string TenantIdClaimType = "tenant_id";

    /// <summary>
    /// Nome do claim que carrega o identificador do Cliente (Customer) no
    /// <c>ClaimsPrincipal</c> autenticado (Fase 10 — Portal do Cliente). Presente
    /// somente para usuários do papel <c>Cliente</c> (R2.1).
    /// </summary>
    public const string CustomerIdClaimType = "customer_id";

    /// <summary>
    /// Nome do papel que identifica um Super Admin da plataforma, autorizado a
    /// operações administrativas entre tenants em endpoints designados (R6.7).
    /// </summary>
    public const string SuperAdminRole = "Super Admin";

    /// <summary>
    /// Cabeçalho HTTP explícito por meio do qual um Super Admin seleciona o
    /// tenant alvo de uma operação administrativa. É honrado exclusivamente
    /// quando o principal é Super Admin; para qualquer outro usuário é ignorado
    /// (R6.7).
    /// </summary>
    public const string AdminTenantHeader = "X-Admin-Tenant-Id";
}
