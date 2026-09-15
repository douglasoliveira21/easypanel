namespace EasyPanel.Modules.Customers;

/// <summary>
/// Dados de entrada para a atualização de um <see cref="Customer"/> do próprio
/// tenant (R8.6). O <c>TenantId</c> e o <c>Id</c> não são alteráveis por aqui: o
/// tenant vem do contexto e o identificador é um parâmetro da operação.
///
/// <para>Se o <see cref="Cnpj"/> mudar, o serviço revalida o formato (R8.4) e
/// reconfere a unicidade por tenant excluindo o próprio registro (R8.5).</para>
/// </summary>
public sealed record UpdateCustomerRequest(
    string RazaoSocial,
    string Cnpj,
    string? NomeFantasia = null,
    string? InscricaoEstadual = null,
    string? Telefone = null,
    string? Email = null,
    string? Endereco = null,
    string? Cidade = null,
    string? Estado = null,
    string? Cep = null,
    string? Observacoes = null);
