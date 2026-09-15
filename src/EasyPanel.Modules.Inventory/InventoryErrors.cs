using EasyPanel.Shared.Kernel.Results;

namespace EasyPanel.Modules.Inventory;

/// <summary>Erros de domínio do módulo de Estoque (Fase 5), no padrão de <c>MonitoringErrors</c>/<c>SupplyErrors</c>.</summary>
public static class InventoryErrors
{
    /// <summary>Recurso inexistente ou de outro tenant. → 404.</summary>
    public static readonly Error NotFound = Error.NotFound(
        "inventory.not_found",
        "Recurso não encontrado.");

    /// <summary>Nome do item ausente (R1.3). → 400.</summary>
    public static readonly Error NameRequired = Error.Validation(
        "inventory.item.name_required",
        "O nome do item é obrigatório.");

    /// <summary>Item inativo não aceita novas movimentações (R2.7). → 400.</summary>
    public static readonly Error ItemInactive = Error.Validation(
        "inventory.item.inactive",
        "O item está inativo e não aceita novas movimentações.");

    /// <summary>Local informado não pertence ao tenant corrente. → 400.</summary>
    public static readonly Error InvalidLocation = Error.Validation(
        "inventory.location.invalid",
        "Local informado é inválido para o tenant.");

    /// <summary>Impressora informada não pertence ao tenant corrente. → 400.</summary>
    public static readonly Error InvalidPrinter = Error.Validation(
        "inventory.printer.invalid",
        "Impressora informada é inválida para o tenant.");

    /// <summary>Quantidade não é um inteiro positivo (R2.2). → 400.</summary>
    public static readonly Error InvalidQuantity = Error.Validation(
        "inventory.movement.invalid_quantity",
        "A quantidade deve ser um inteiro positivo.");

    /// <summary>Ajuste sem justificativa ou sem direção (R2.5). → 400.</summary>
    public static readonly Error AdjustmentRequiresReason = Error.Validation(
        "inventory.movement.adjustment_requires_reason",
        "Um ajuste exige justificativa e direção (aumento/redução).");

    /// <summary>Saída com saldo insuficiente (R2.4). → 409.</summary>
    public static readonly Error InsufficientBalance = Error.Conflict(
        "inventory.movement.insufficient_balance",
        "Saldo insuficiente para a saída solicitada.");

    /// <summary>Percentual/quantidade mínima fora do intervalo permitido. → 400.</summary>
    public static readonly Error InvalidMinimum = Error.Validation(
        "inventory.minimum.invalid",
        "A quantidade mínima deve ser maior ou igual a zero.");
}
