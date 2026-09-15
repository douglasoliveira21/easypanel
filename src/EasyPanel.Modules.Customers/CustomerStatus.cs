namespace EasyPanel.Modules.Customers;

/// <summary>
/// Conjunto fechado de estados de um <see cref="Customer"/> (R8.3). Persistido
/// como <c>int</c> pela convenção do <c>AppDbContext</c>. A ordem dos valores é
/// estável e não deve ser reordenada, pois os inteiros são gravados no banco.
/// </summary>
public enum CustomerStatus
{
    /// <summary>Cliente ativo (padrão na criação).</summary>
    Ativo = 0,

    /// <summary>Cliente inativo.</summary>
    Inativo = 1,

    /// <summary>Cliente bloqueado.</summary>
    Bloqueado = 2,
}
