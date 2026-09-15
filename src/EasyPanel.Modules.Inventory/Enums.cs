namespace EasyPanel.Modules.Inventory;

/// <summary>Tipo de uma <see cref="InventoryMovement"/> (R2.1). Persistido como int.</summary>
public enum InventoryMovementType
{
    /// <summary>Entrada de itens no Local.</summary>
    Entrada = 0,

    /// <summary>Saída de itens do Local.</summary>
    Saida = 1,

    /// <summary>Correção administrativa de saldo, com justificativa obrigatória (R2.5).</summary>
    Ajuste = 2,
}

/// <summary>
/// Direção de uma movimentação do tipo <see cref="InventoryMovementType.Ajuste"/>
/// (R2.5). Irrelevante para Entrada/Saída, cuja direção já vem do
/// <see cref="InventoryMovementType"/>.
/// </summary>
public enum AdjustmentDirection
{
    /// <summary>Ajuste que aumenta o saldo.</summary>
    Increase = 0,

    /// <summary>Ajuste que reduz o saldo (nunca abaixo de zero).</summary>
    Decrease = 1,
}
