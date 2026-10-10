using ExamPlatform.Modules.Analytics.Domain;
using ExamPlatform.Modules.Analytics.Domain.Exceptions;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The two measures analytics shows a candidate (FR-36), and the tally they are worked from.</summary>
public class PerformanceMeasuresTests
{
    [Fact]
    public void PercentOfMarks_IsTheShareOfTheMarksAvailable()
    {
        Assert.Equal(75m, PerformanceMeasures.PercentOfMarks(30m, 40m));
    }

    [Fact]
    public void PercentOfMarks_RoundsHalfAwayFromZero_TheRoundingTheMarksUse()
    {
        // 2 / 3 is 66.666...; two decimals keep 66.67, and 1 / 8 is exactly 12.5 which must not round to even (12).
        Assert.Equal(66.67m, PerformanceMeasures.PercentOfMarks(2m, 3m));
        Assert.Equal(12.50m, PerformanceMeasures.PercentOfMarks(1m, 8m));
    }

    [Fact]
    public void PercentOfMarks_IsNotClampedAtZero_SoANegativeSittingShowsAsOne()
    {
        // With negative marking a result can earn less than nothing; showing zero would hide a bad sitting.
        Assert.Equal(-12.5m, PerformanceMeasures.PercentOfMarks(-5m, 40m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void PercentOfMarks_IsNull_WhenNoMarksWereAvailable(decimal maxScore)
    {
        Assert.Null(PerformanceMeasures.PercentOfMarks(3m, maxScore));
    }

    [Fact]
    public void Accuracy_IsTheShareOfTheAnsweredQuestionsThatWereFullyCorrect()
    {
        Assert.Equal(75m, PerformanceMeasures.Accuracy(correct: 3, wrong: 1, partial: 0));
    }

    [Fact]
    public void Accuracy_CountsAPartlyRightAnswerAsAnsweredButNotCorrect()
    {
        Assert.Equal(50m, PerformanceMeasures.Accuracy(correct: 2, wrong: 0, partial: 2));
    }

    [Fact]
    public void Accuracy_IsNull_WhenNothingWasAnswered()
    {
        // A skipped question is not in the figure at all, so a paper with nothing answered has no accuracy rather than 0%.
        Assert.Null(PerformanceMeasures.Accuracy(correct: 0, wrong: 0, partial: 0));
    }

    [Fact]
    public void AnswerTally_Answered_LeavesOutTheUnanswered()
    {
        var tally = new AnswerTally(Correct: 2, Wrong: 1, Partial: 1, Unanswered: 5);

        Assert.Equal(4, tally.Answered);
        Assert.Equal(50m, tally.Accuracy);
    }

    [Fact]
    public void AnswerTally_Of_RefusesANegativeCount_SoNoAccuracyIsShownFromBadFigures()
    {
        var error = Assert.Throws<InvalidAnswerTallyError>(() => AnswerTally.Of(correct: 1, wrong: -1, partial: 0, unanswered: 0));

        Assert.Equal("wrong", error.CountName);
        Assert.Equal("invalid_answer_tally", error.ErrorCode);
    }

    [Fact]
    public void AnswerTally_Add_SumsEveryCount()
    {
        var total = AnswerTally.Empty
            .Add(new AnswerTally(1, 0, 0, 2))
            .Add(new AnswerTally(0, 3, 1, 0));

        Assert.Equal(new AnswerTally(1, 3, 1, 2), total);
        Assert.Null(AnswerTally.Empty.Accuracy);
    }
}
