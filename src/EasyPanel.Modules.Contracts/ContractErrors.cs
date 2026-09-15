using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Contracts;

/// <summary>Erros de domínio do módulo de Contratos (Fase 7), no padrão de <c>TicketingErrors</c>/<c>InventoryErrors</c>.</summary>
public static class ContractErrors
{
    /// <summary>Recurso inexistente ou de outro tenant. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "contracts.not_found",
        "Recurso não encontrado.");

    /// <summary>Número do contrato ausente (R1.3). → 400.</summary>
    public static readonly Error NumberRequired = Error.Validation(
        "contracts.contract.number_required",
        "O número do contrato é obrigatório.");

    /// <summary>Cliente informado não pertence ao tenant corrente (R1.3). → 400.</summary>
    public static readonly Error InvalidCustomer = Error.Validation(
        "contracts.contract.invalid_customer",
        "Cliente informado é inválido para o tenant.");

    /// <summary>Data de fim anterior à data de início (R1.3). → 400.</summary>
    public static readonly Error InvalidDateRange = Error.Validation(
        "contracts.contract.invalid_date_range",
        "A data de fim da vigência não pode ser anterior à data de início.");

    /// <summary>Transição de status não permitida a partir do status corrente (R5.2). → 400.</summary>
    public static readonly Error InvalidStatusTransition = Error.Validation(
        "contracts.contract.invalid_status_transition",
        "Transição de status não permitida a partir do status corrente.");

    /// <summary>Local/Impressora informado não pertence ao mesmo Cliente do contrato (R2.3). → 400.</summary>
    public static readonly Error ScopeCustomerMismatch = Error.Validation(
        "contracts.scope.customer_mismatch",
        "O Local/Impressora informado não pertence ao mesmo Cliente do contrato.");

    /// <summary>Local/Impressora já vinculado a outro contrato do mesmo Cliente com vigência sobreposta (R2.4). → 409.</summary>
    public static readonly Error ScopeOverlap = Error.Conflict(
        "contracts.scope.overlap",
        "O Local/Impressora já está vinculado a outro contrato do mesmo Cliente com vigência sobreposta.");

    /// <summary>Quantidade incluída ou preço de excedente negativos (R3.2). → 400.</summary>
    public static readonly Error InvalidFranchise = Error.Validation(
        "contracts.franchise.invalid",
        "A quantidade incluída e o preço do excedente devem ser maiores ou iguais a zero.");
}
