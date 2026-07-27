using GymLink.Application.Common;

namespace GymLink.Application.Tests;

public sealed class PaginationTests
{
    [Fact]
    public void Defaults_are_bounded()
    {
        var request = new PagedRequest();

        request.Validate();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(100, PagedRequest.MaximumPageSize);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Invalid_bounds_are_rejected(int page, int pageSize)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize };

        Assert.Throws<ArgumentOutOfRangeException>(request.Validate);
    }

    [Fact]
    public void Total_pages_round_up()
    {
        var result = new PagedResult<int>([], 1, 20, 41);

        Assert.Equal(3, result.TotalPages);
    }
}
