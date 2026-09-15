using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Customers;

/// <summary>
/// Local (ponto de instalação) pertencente a um <see cref="Customer"/> (R9). É
/// uma <see cref="TenantEntity"/> — portanto recebe automaticamente o filtro
/// global de consulta por <c>TenantId</c> (leituras auto-escopadas ao tenant
/// corrente — R9.6/R9.7) e a validação de escrita do interceptor de
/// <c>SaveChanges</c> (o <c>TenantId</c> é carimbado a partir do
/// <c>ITenantContext</c> em <c>Add</c>; escritas cross-tenant são rejeitadas —
/// R6/R9).
///
/// <para>Cada Local pertence a exatamente um <see cref="Customer"/> (via
/// <see cref="CustomerId"/>), e um Customer pode possuir múltiplos Locais (R9.3).
/// O <see cref="Id"/>, <see cref="BaseEntity.CreatedAt"/>,
/// <see cref="BaseEntity.UpdatedAt"/> e o <c>TenantId</c> vêm das classes base.</para>
/// </summary>
public class Location : TenantEntity
{
    /// <summary>Cliente ao qual o local pertence (R9.1/R9.3).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Nome do local (obrigatório) — R9.2.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Endereço (opcional) — R9.2.</summary>
    public string? Endereco { get; set; }

    /// <summary>Responsável pelo local (opcional) — R9.2.</summary>
    public string? Responsavel { get; set; }

    /// <summary>Telefone (opcional) — R9.2.</summary>
    public string? Telefone { get; set; }

    /// <summary>Email (opcional) — R9.2.</summary>
    public string? Email { get; set; }

    /// <summary>Observações livres (opcional) — R9.2.</summary>
    public string? Observacoes { get; set; }

    /// <summary>Status do local, restrito ao conjunto fechado (R9.8).</summary>
    public LocationStatus Status { get; set; } = LocationStatus.Ativo;
}
