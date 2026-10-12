using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The average time per question, worked from the reported times (FR-36).</summary>
public class QuestionTimingTests
{
    [Fact]
    public void WithNoReportedQuestion_TheAverageIsNull_NotZero()
    {
        Assert.Null(QuestionTiming.AverageOf([]));
    }

    [Fact]
    public void TheAverageIsWorkedFromTheTotal_AndRoundedToOneDecimal()
    {
        // 10 + 10 + 11 = 31 over three questions is 10.333..., which rounds to 10.3.
        Assert.Equal(10.3m, QuestionTiming.AverageOf([10, 10, 11]));
    }

    [Fact]
    public void AnExactMidpoint_RoundsAwayFromZero()
    {
        // 9 over four questions is 2.25 exactly; half is rounded up, not to the even digit, so the figure reads 2.3.
        Assert.Equal(2.3m, QuestionTiming.AverageOf([2, 2, 2, 3]));
    }
}
