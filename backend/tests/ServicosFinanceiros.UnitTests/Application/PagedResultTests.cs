using FluentAssertions;
using ServicosFinanceiros.Application.Accounts;

namespace ServicosFinanceiros.UnitTests.Application;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(5, 0, 0)]
    public void TotalPages_ShouldRoundUpAndHandleEmptyPageSize(int totalItems, int pageSize, int expected)
    {
        var result = new PagedResult<int>([], Page: 1, pageSize, totalItems);

        result.TotalPages.Should().Be(expected);
    }
}
