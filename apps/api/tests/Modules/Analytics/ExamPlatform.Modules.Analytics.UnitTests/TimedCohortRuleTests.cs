using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Contracts;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The minimum cohort rule for a question's average time (FR-37): when the average is shown, and when it is withheld.</summary>
public class TimedCohortRuleTests
{
    [Fact]
    public void BelowTheMinimum_TheAverageIsWithheld_AndTheCountIsStillShown()
    {
        var figure = TimedCohortRule.FigureOf([10, 20, 30], minimumCohortSize: 4);

        Assert.Equal(3, figure.CandidatesTimed);
        Assert.Null(figure.AverageSeconds);
        Assert.Equal(ItemTimeStatus.BelowMinimumCohort, figure.Status);
    }

    [Fact]
    public void AtTheMinimum_TheAverageIsShown()
    {
        var figure = TimedCohortRule.FigureOf([10, 20, 30, 40], minimumCohortSize: 4);

        Assert.Equal(4, figure.CandidatesTimed);
        Assert.Equal(25m, figure.AverageSeconds);
        Assert.Equal(ItemTimeStatus.Shown, figure.Status);
    }

    [Fact]
    public void AboveTheMinimum_TheAverageIsShown()
    {
        var figure = TimedCohortRule.FigureOf([10, 20, 30, 40, 50], minimumCohortSize: 4);

        Assert.Equal(5, figure.CandidatesTimed);
        Assert.Equal(30m, figure.AverageSeconds);
        Assert.Equal(ItemTimeStatus.Shown, figure.Status);
    }

    [Fact]
    public void AQuestionWithNoTimes_HasNoAverage_AndSaysThereAreNoTimes_NotZero()
    {
        var figure = TimedCohortRule.FigureOf([], minimumCohortSize: 30);

        Assert.Equal(0, figure.CandidatesTimed);
        Assert.Null(figure.AverageSeconds);
        Assert.Equal(ItemTimeStatus.NoTimes, figure.Status);
    }
}
