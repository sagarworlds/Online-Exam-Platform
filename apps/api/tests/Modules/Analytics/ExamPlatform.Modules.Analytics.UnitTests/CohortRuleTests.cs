using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The cohort a candidate's result is compared with, and the topper and average it gives (FR-36).</summary>
public class CohortRuleTests
{
    private static readonly DateTime Day1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static CohortAttempt Sitting(Guid candidate, decimal score, decimal maxScore = 100m, int day = 1) =>
        new(candidate, score, maxScore, Day1.AddDays(day - 1));

    /// <summary>Candidates who each sat the exam with the same score, so the cohort is exactly <paramref name="count"/> people.</summary>
    private static CohortAttempt[] Crowd(int count, decimal score, decimal maxScore = 100m) =>
        Enumerable.Range(0, count).Select(_ => Sitting(Guid.NewGuid(), score, maxScore)).ToArray();

    [Fact]
    public void ACohortOfTwentyNine_IsBelowThirty_AndWorksOutNoFigure()
    {
        var figures = CohortRule.Of(Crowd(29, 80m), 30);

        Assert.Equal(CohortStatus.BelowMinimum, figures.Status);
        Assert.Null(figures.Size);
        Assert.Null(figures.TopperPercent);
        Assert.Null(figures.AveragePercent);
    }

    [Fact]
    public void ACohortOfThirty_IsAtTheMinimum_AndShowsTheTopperAndTheAverage()
    {
        var cohort = Crowd(29, 80m).Append(Sitting(Guid.NewGuid(), 60m)).ToArray();

        var figures = CohortRule.Of(cohort, 30);

        Assert.Equal(CohortStatus.Shown, figures.Status);
        Assert.Equal(30, figures.Size);
        Assert.Equal(80m, figures.TopperPercent);
        Assert.Equal(79.33m, figures.AveragePercent);
    }

    [Fact]
    public void TheThreshold_IsInclusive_NineBelowTenIsHeldAndTenIsShown()
    {
        Assert.Equal(CohortStatus.BelowMinimum, CohortRule.Of(Crowd(9, 50m), 10).Status);
        Assert.Equal(CohortStatus.Shown, CohortRule.Of(Crowd(10, 50m), 10).Status);
    }

    [Fact]
    public void AnEmptyCohort_IsBelowTheMinimum_EvenWhenTheMinimumIsZero()
    {
        // With no candidate there is no topper to take, so the empty cohort is held back rather than failing on a maximum of nothing.
        var figures = CohortRule.Of([], 0);

        Assert.Equal(CohortStatus.BelowMinimum, figures.Status);
        Assert.Null(figures.TopperPercent);
    }

    [Fact]
    public void ACandidateWhosePaperHadNoMarks_IsNotCountedTowardTheMinimum()
    {
        // Twenty-nine candidates have a percentage and one paper had no marks, so there are 29 figures to stand on, not 30.
        var cohort = Crowd(29, 80m).Append(Sitting(Guid.NewGuid(), 0m, maxScore: 0m)).ToArray();

        var figures = CohortRule.Of(cohort, 30);

        Assert.Equal(CohortStatus.BelowMinimum, figures.Status);
        Assert.Null(figures.Size);
    }

    [Fact]
    public void ACandidateWhoSatTwice_IsInTheCohortOnce_ByTheirBestResult()
    {
        // Thirty candidates, one of whom sat twice: the retake of 5 of 8 (62.5%) is the one that counts, so the size is 30, not 31.
        var retaker = Guid.NewGuid();
        var cohort = Crowd(29, 8m, maxScore: 8m).Concat([Sitting(retaker, 2m, 8m, day: 1), Sitting(retaker, 5m, 8m, day: 2)]).ToArray();

        var figures = CohortRule.Of(cohort, 30);

        Assert.Equal(30, figures.Size);
        Assert.Equal(100m, figures.TopperPercent);
        Assert.Equal(98.75m, figures.AveragePercent);
    }

    [Fact]
    public void TheTopper_IsTheHighestPercentOfMarks_NotTheHighestRawScore()
    {
        // A scored 6 of 8 (75%) and B scored 20 of 40 (50%): B has the higher raw score but A has the higher percentage, so A is the topper.
        var cohort = Crowd(28, 50m).Concat([Sitting(Guid.NewGuid(), 6m, 8m), Sitting(Guid.NewGuid(), 20m, 40m)]).ToArray();

        var figures = CohortRule.Of(cohort, 30);

        Assert.Equal(75m, figures.TopperPercent);
    }

    [Fact]
    public void TheAverage_IsTheMeanOfThePercentages_RoundedToTwoDecimals()
    {
        // Twenty-nine at 50% and one at 0% average 48.33..., which is shown to two decimals.
        var cohort = Crowd(29, 50m).Append(Sitting(Guid.NewGuid(), 0m)).ToArray();

        var figures = CohortRule.Of(cohort, 30);

        Assert.Equal(48.33m, figures.AveragePercent);
    }

    [Fact]
    public void ACandidateWhoseHighestRawScoreIsNotTheirHighestPercent_IsRankedByThePercent()
    {
        // X scored 6 of 8 (75%) on one paper and 20 of 40 (50%) on another. X's highest raw score is 20, but the comparison is in percent, so X's
        // result is 75%. With 29 others at 50%, the topper is 75 and the average is (75 + 29 x 50) / 30 = 50.83. Ranked by raw score it would be 50.
        var x = Guid.NewGuid();
        var cohort = Crowd(29, 50m).Concat([Sitting(x, 6m, 8m, day: 1), Sitting(x, 20m, 40m, day: 2)]).ToArray();

        var figures = CohortRule.Of(cohort, 30);

        Assert.Equal(30, figures.Size);
        Assert.Equal(75m, figures.TopperPercent);
        Assert.Equal(50.83m, figures.AveragePercent);
    }

    [Fact]
    public void BestOf_TakesTheHighestPercent_NotTheHighestRawScore()
    {
        var candidate = Guid.NewGuid();
        var sixOfEight = Sitting(candidate, 6m, 8m, day: 1);
        var twentyOfForty = Sitting(candidate, 20m, 40m, day: 2);

        Assert.Equal(sixOfEight, CohortRule.BestOf([sixOfEight, twentyOfForty]));
    }

    [Fact]
    public void BestOf_OnATiedPercent_TakesTheHigherScore_ThenTheLaterSubmission()
    {
        var candidate = Guid.NewGuid();
        var fourOfEight = Sitting(candidate, 4m, 8m, day: 1);
        var fiftyOfHundred = Sitting(candidate, 50m, 100m, day: 2);
        var eightOfSixteenEarlier = Sitting(candidate, 8m, 16m, day: 1);
        var eightOfSixteenLater = Sitting(candidate, 8m, 16m, day: 3);

        // Both are 50%; the 50 out of 100 earned more marks than the 4 out of 8.
        Assert.Equal(fiftyOfHundred, CohortRule.BestOf([fourOfEight, fiftyOfHundred]));

        // Same percent and same score: the later submission is the one kept.
        Assert.Equal(eightOfSixteenLater, CohortRule.BestOf([eightOfSixteenEarlier, eightOfSixteenLater]));
    }

    [Fact]
    public void BestOf_RanksAResultWithNoMarksBelowEveryPercentage()
    {
        // The paper with no marks has no percent, so it must not displace the 25% result, even though it was submitted later.
        var candidate = Guid.NewGuid();
        var quarter = Sitting(candidate, 2m, 8m, day: 1);
        var noMarks = Sitting(candidate, 0m, 0m, day: 3);

        Assert.Equal(quarter, CohortRule.BestOf([noMarks, quarter]));
    }

    [Fact]
    public void BestOf_KeepsTheHighestPercent_AndTheLaterSubmissionWhenTheyTie()
    {
        var candidate = Guid.NewGuid();
        var lower = Sitting(candidate, 4m, day: 1);
        var higher = Sitting(candidate, 7m, day: 2);
        var tieLater = Sitting(candidate, 7m, day: 3);

        Assert.Equal(higher, CohortRule.BestOf([lower, higher]));
        Assert.Equal(tieLater, CohortRule.BestOf([higher, tieLater]));
    }

    [Fact]
    public void BestOf_IsNullWhenTheCandidateHasNoResult()
    {
        Assert.Null(CohortRule.BestOf([]));
    }
}
