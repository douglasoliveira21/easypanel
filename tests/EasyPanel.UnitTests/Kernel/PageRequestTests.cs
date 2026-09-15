using EasyPanel.Shared.Kernel.Pagination;

namespace EasyPanel.UnitTests.Kernel;

/// <summary>
/// Unit tests para o clamping de <see cref="PageRequest"/>, garantindo o
/// limite máximo de página de 100 registros (R12.4) e a normalização de
/// valores fora do intervalo válido.
/// </summary>
public class PageRequestTests
{
    [Fact]
    public void Defaults_AreApplied()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(PageRequest.DefaultPageSize, request.PageSize);
        Assert.Null(request.Sort);
        Assert.Null(request.Search);
    }

    [Fact]
    public void PageSize_AboveMax_IsClampedToMax()
    {
        var request = new PageRequest(PageSize: 500);

        Assert.Equal(PageRequest.MaxPageSize, request.PageSize);
    }

    [Fact]
    public void PageSize_ExactlyMax_IsPreserved()
    {
        var request = new PageRequest(PageSize: PageRequest.MaxPageSize);

        Assert.Equal(PageRequest.MaxPageSize, request.PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void PageSize_BelowOne_FallsBackToDefault(int pageSize)
    {
        var request = new PageRequest(PageSize: pageSize);

        Assert.Equal(PageRequest.DefaultPageSize, request.PageSize);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(25, 25)]
    [InlineData(99, 99)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public void PageSize_IsNeverAboveMax(int requested, int expected)
    {
        var request = new PageRequest(PageSize: requested);

        Assert.Equal(expected, request.PageSize);
        Assert.True(request.PageSize <= PageRequest.MaxPageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Page_BelowOne_IsNormalizedToOne(int page)
    {
        var request = new PageRequest(Page: page);

        Assert.Equal(PageRequest.MinPage, request.Page);
    }

    [Fact]
    public void Page_ValidValue_IsPreserved()
    {
        var request = new PageRequest(Page: 7);

        Assert.Equal(7, request.Page);
    }

    [Fact]
    public void WithExpression_ReClampsPageSize()
    {
        var request = new PageRequest();

        var mutated = request with { PageSize = 250 };

        Assert.Equal(PageRequest.MaxPageSize, mutated.PageSize);
    }

    [Fact]
    public void SortAndSearch_ArePreserved()
    {
        var request = new PageRequest(Sort: "razaoSocial desc", Search: "acme");

        Assert.Equal("razaoSocial desc", request.Sort);
        Assert.Equal("acme", request.Search);
    }
}
