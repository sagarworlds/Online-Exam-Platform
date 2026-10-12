using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// The shared errors that other modules reuse as they are: a lost-update race answers 409 with one code everywhere,
/// and a page request a list will not serve answers 400.
/// </summary>
public class SharedDomainErrorTests
{
    [Fact]
    public void ConcurrencyConflict_DefaultsToAGenericMessage_AndReports409()
    {
        var error = new ConcurrencyConflictError();

        Assert.Equal("concurrency_conflict", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Contains("changed by another request", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void ConcurrencyConflict_KeepsTheProviderException()
    {
        var cause = new InvalidOperationException("provider detail");

        var error = new ConcurrencyConflictError(cause);

        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void InvalidPageRequest_ReportsItsCodeAndA400_WithTheGivenMessage()
    {
        var error = new InvalidPageRequestError("Page size must be at most 100.");

        Assert.Equal("invalid_page_request", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
        Assert.Equal("Page size must be at most 100.", error.Message);
    }
}
