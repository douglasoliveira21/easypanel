using EasyPanel.Shared.Kernel.Entities;

namespace EasyPanel.Modules.Contracts;

/// <summary>
/// Contrato comercial de um Cliente (Fase 7 — R1), com vigência e ciclo de
/// vida de status. Cliente é referenciado apenas por <c>Guid</c>.
/// </summary>
public class Contract : TenantEntity
{
    /// <summary>Número/nome do contrato.</summary>
    public required string Number { get; set; }

    /// <summary>Cliente ao qual o contrato pertence (R1.3). Imutável após a criação.</summary>
    public required Guid CustomerId { get; set; }

    /// <summary>Data de início da vigência.</summary>
    public required DateTimeOffset StartDate { get; set; }

    /// <summary>Cursor portável de <see cref="StartDate"/> — SQLite não traduz comparação sobre <c>DateTimeOffset</c>.</summary>
    public long StartDateTicks { get; set; }

    /// <summary>Data de fim da vigência; nulo = prazo indeterminado.</summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>Cursor portável de <see cref="EndDate"/>.</summary>
    public long? EndDateTicks { get; set; }

    /// <summary>Status corrente do ciclo de vida.</summary>
    public ContractStatus Status { get; set; } = ContractStatus.Rascunho;

    /// <summary>Observações livres.</summary>
    public string? Observations { get; set; }

    /// <summary>Cursor portável de <c>CreatedAt</c> (herdado de <see cref="BaseEntity"/>), para ordenação por listagem.</summary>
    public long CreatedAtTicks { get; set; }
}
