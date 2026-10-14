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

    [Theory]
    [InlineData(5, 2, 2)]
    [InlineData(5, 4, 4)]
    [InlineData(5, 1, 1)]
    [InlineData(2, 1, 1)]
    public void AWarningPointTheAuthorSetIsHonouredWhenItFitsTheLimit(int limit, int warnAt, int expected) =>
        Assert.Equal(expected, FocusWarningPoint.Effective(limit, warnAt));

    [Theory]
    [InlineData(5, null, 3)]
    [InlineData(2, null, 1)]
    [InlineData(0, 2, 0)]
    [InlineData(0, null, 0)]
    public void WithoutAWarningPointOrWithoutAWatch_TheDefaultOrNothingApplies(int limit, int? warnAt, int expected) =>
        Assert.Equal(expected, FocusWarningPoint.Effective(limit, warnAt));

    [Theory]
    [InlineData(5, 3, true)]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(5, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(0, 1, false)]
    public void AWarningPointIsAllowedFromTheFirstDepartureToTheOneBeforeTheLimit(int limit, int warnAt, bool expected) =>
        Assert.Equal(expected, FocusWarningPoint.IsAllowed(warnAt, limit));
}
