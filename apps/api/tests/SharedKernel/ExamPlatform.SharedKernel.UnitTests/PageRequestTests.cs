using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.SharedKernel.UnitTests;

public class PageRequestTests
{
    [Fact]
    public void Create_WithNothingGiven_ServesTheFirstPageOfTheDefaultSize()
    {
        var request = PageRequest.Create(null, null);

        Assert.Equal(1, request.Page);
        Assert.Equal(PageRequest.DefaultPageSize, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Fact]
    public void Skip_AtTheLastPageAndLargestSize_StillFitsAnInt()
    {
        Assert.Equal((PageRequest.MaxPage - 1) * PageRequest.MaxPageSize, PageRequest.Create(PageRequest.MaxPage, PageRequest.MaxPageSize).Skip);
    }

    [Fact]
    public void Skip_IsTheRowsBeforeThePage()
    {
        Assert.Equal(300, PageRequest.Create(4, 100).Skip);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(PageRequest.MaxPageSize)]
    public void Create_AcceptsTheEdgesOfThePageSizeRange(int pageSize)
    {
        Assert.Equal(pageSize, PageRequest.Create(1, pageSize).PageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PageRequest.MaxPage + 1)]
    [InlineData(int.MaxValue)]
    public void Create_WithAPageOutsideTheRange_ThrowsInvalidPageRequestError(int page)
    {
        Assert.Throws<InvalidPageRequestError>(() => PageRequest.Create(page, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(PageRequest.MaxPageSize + 1)]
    public void Create_WithAPageSizeOutsideTheRange_ThrowsInvalidPageRequestError(int pageSize)
    {
        Assert.Throws<InvalidPageRequestError>(() => PageRequest.Create(null, pageSize));
    }
}
