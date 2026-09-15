namespace EasyPanel.Shared.Kernel.Pagination;

/// <summary>
/// Parâmetros de uma requisição de listagem paginada. O <see cref="PageSize"/>
/// é limitado (clamped) a no máximo <see cref="MaxPageSize"/> registros por
/// requisição, garantindo o limite de escala definido em R12.4. O clamping é
/// aplicado no acessor <c>init</c>, de modo que vale para qualquer forma de
/// construção (construtor primário ou expressão <c>with</c>).
/// </summary>
/// <param name="Page">Número da página solicitada (base 1).</param>
/// <param name="PageSize">Tamanho de página solicitado; será limitado ao intervalo válido.</param>
/// <param name="Sort">Expressão de ordenação opcional.</param>
/// <param name="Search">Termo de busca opcional.</param>
public sealed record PageRequest(int Page = 1, int PageSize = 25, string? Sort = null, string? Search = null)
{
    /// <summary>Número máximo de registros permitidos por página (R12.4).</summary>
    public const int MaxPageSize = 100;

    /// <summary>Tamanho de página padrão quando não especificado.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Página mínima válida (base 1).</summary>
    public const int MinPage = 1;

    private readonly int _page = NormalizePage(Page);
    private readonly int _pageSize = NormalizePageSize(PageSize);

    /// <summary>
    /// Número da página (base 1). Valores abaixo de <see cref="MinPage"/> são
    /// normalizados para <see cref="MinPage"/>.
    /// </summary>
    public int Page
    {
        get => _page;
        init => _page = NormalizePage(value);
    }

    /// <summary>
    /// Tamanho da página efetivo. Valores acima de <see cref="MaxPageSize"/>
    /// são limitados a <see cref="MaxPageSize"/>; valores menores que 1 são
    /// normalizados para <see cref="DefaultPageSize"/>.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = NormalizePageSize(value);
    }

    private static int NormalizePage(int page) => page < MinPage ? MinPage : page;

    private static int NormalizePageSize(int pageSize) =>
        pageSize < 1 ? DefaultPageSize
        : pageSize > MaxPageSize ? MaxPageSize
        : pageSize;
}
