namespace EasyPanel.Shared.Kernel.Exceptions;

/// <summary>
/// Lançada quando uma operação tenta ler ou escrever um registro cujo
/// TenantId difere do contexto autenticado. Mapeada para HTTP 404 a fim de
/// não revelar a existência de recursos de outros tenants (R6.5).
/// </summary>
public sealed class CrossTenantAccessException : DomainException
{
    /// <summary>Cria a exceção com uma mensagem padrão.</summary>
    public CrossTenantAccessException()
        : base("Acesso a dados de outro tenant foi recusado.")
    {
    }

    /// <summary>Cria a exceção com uma mensagem descritiva.</summary>
    public CrossTenantAccessException(string message)
        : base(message)
    {
    }
}
