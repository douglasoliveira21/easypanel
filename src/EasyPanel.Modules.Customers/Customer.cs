using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Customers;

/// <summary>
/// Cliente: empresa atendida por um tenant, alvo dos serviços de outsourcing
/// (R8). É uma <see cref="TenantEntity"/> — portanto recebe automaticamente o
/// filtro global de consulta por <c>TenantId</c> (leituras auto-escopadas ao
/// tenant corrente — R8.7/R8.8) e a validação de escrita do interceptor de
/// <c>SaveChanges</c> (o <c>TenantId</c> é carimbado a partir do
/// <c>ITenantContext</c> em <c>Add</c>; escritas cross-tenant são rejeitadas —
/// R6/R8).
///
/// <para>O <see cref="Cnpj"/> é sempre armazenado <b>normalizado</b> (somente
/// dígitos, 14 caracteres), garantindo unicidade estável por tenant via o índice
/// único composto <c>(TenantId, Cnpj)</c> (R8.5). O <see cref="Id"/>,
/// <see cref="BaseEntity.CreatedAt"/>, <see cref="BaseEntity.UpdatedAt"/> e o
/// <c>TenantId</c> vêm das classes base.</para>
/// </summary>
public class Customer : TenantEntity
{
    /// <summary>Razão social (obrigatório) — R8.2.</summary>
    public string RazaoSocial { get; set; } = string.Empty;

    /// <summary>Nome fantasia (opcional) — R8.2.</summary>
    public string? NomeFantasia { get; set; }

    /// <summary>CNPJ normalizado (somente dígitos, 14 caracteres) — R8.2/R8.4.</summary>
    public string Cnpj { get; set; } = string.Empty;

    /// <summary>Inscrição estadual (opcional) — R8.2.</summary>
    public string? InscricaoEstadual { get; set; }

    /// <summary>Telefone (opcional) — R8.2.</summary>
    public string? Telefone { get; set; }

    /// <summary>Email (opcional) — R8.2.</summary>
    public string? Email { get; set; }

    /// <summary>Endereço (opcional) — R8.2.</summary>
    public string? Endereco { get; set; }

    /// <summary>Cidade (opcional) — R8.2.</summary>
    public string? Cidade { get; set; }

    /// <summary>Estado/UF (opcional) — R8.2.</summary>
    public string? Estado { get; set; }

    /// <summary>CEP (opcional) — R8.2.</summary>
    public string? Cep { get; set; }

    /// <summary>Observações livres (opcional) — R8.2.</summary>
    public string? Observacoes { get; set; }

    /// <summary>Status do cliente, restrito ao conjunto fechado (R8.3).</summary>
    public CustomerStatus Status { get; set; } = CustomerStatus.Ativo;
}
