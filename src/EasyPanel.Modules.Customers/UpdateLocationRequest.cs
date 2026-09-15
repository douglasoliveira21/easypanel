namespace EasyPanel.Modules.Customers;

/// <summary>
/// Dados de entrada para a atualização de um <see cref="Location"/> do próprio
/// tenant (R9.5). O <c>TenantId</c>, o <c>Id</c> e o vínculo com o
/// <see cref="Location.CustomerId"/> não são alteráveis por aqui: o tenant vem
/// do contexto, o identificador é um parâmetro da operação e o local permanece
/// vinculado ao mesmo cliente. O status é alterado pelo endpoint dedicado (R9.8).
/// </summary>
public sealed record UpdateLocationRequest(
    string Nome,
    string? Endereco = null,
    string? Responsavel = null,
    string? Telefone = null,
    string? Email = null,
    string? Observacoes = null);
