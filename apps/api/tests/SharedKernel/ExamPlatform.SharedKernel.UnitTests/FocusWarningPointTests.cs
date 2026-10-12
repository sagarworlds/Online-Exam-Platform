using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// When a candidate is first warned about leaving the exam page (FR-22, requirements section 8): from the third departure, moved down
/// only when the author's limit is too small to leave a warning before it.
/// </summary>
public class FocusWarningPointTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    [InlineData(20, 3)]
    public void TheWarningPointIsTheThirdDepartureUnlessTheLimitIsTooSmall(int limit, int expected)
    {
        Assert.Equal(expected, FocusWarningPoint.For(limit));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(20)]
    public void TheWarningPointIsAlwaysBeforeTheDepartureThatEndsTheAttempt(int limit)
    {
        Assert.True(FocusWarningPoint.For(limit) < limit);
    }
}
