namespace EasyPanel.Shared.Kernel.Pagination;

/// <summary>
/// Página de resultados de uma consulta de listagem. Contém os itens da
/// página atual e os metadados de paginação (R12.4).
/// </summary>
/// <typeparam name="T">Tipo dos itens paginados.</typeparam>
/// <param name="Items">Itens da página atual.</param>
/// <param name="Page">Número da página (base 1).</param>
/// <param name="PageSize">Tamanho da página efetivamente aplicado.</param>
/// <param name="TotalCount">Total de registros disponíveis, ignorando a paginação.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
