namespace EasyPanel.Modules.Customers;

/// <summary>
/// Conjunto fechado de estados de um <see cref="Location"/> (R9.8). Persistido
/// como <c>int</c> pela convenção do <c>AppDbContext</c>. A ordem dos valores é
/// estável e não deve ser reordenada, pois os inteiros são gravados no banco.
/// </summary>
public enum LocationStatus
{
    /// <summary>Local ativo (padrão na criação).</summary>
    Ativo = 0,

    /// <summary>Local inativo.</summary>
    Inativo = 1,
}
