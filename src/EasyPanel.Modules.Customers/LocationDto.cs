namespace EasyPanel.Modules.Customers;

/// <summary>
/// Projeção de leitura de um <see cref="Location"/> exposta pelo
/// <see cref="ILocationService"/> (R9/R12.1). Contrato de domínio distinto da
/// entidade de persistência.
/// </summary>
public sealed record LocationDto(
    Guid Id,
    Guid TenantId,
    Guid CustomerId,
    string Nome,
    string? Endereco,
    string? Responsavel,
    string? Telefone,
    string? Email,
    string? Observacoes,
    LocationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
