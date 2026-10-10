using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The rank and percentile of a result among the other candidates' best results (FR-32). These are the rules the result page and the
/// leaderboard both show, so each one is pinned here: ties, the first place, a cohort of one, and rounding.
/// </summary>
public class ResultStandingTests
{
    [Fact]
    public void AResultWithNoOneElse_IsFirstOfOne_AndTheHundredthPercentile()
    {
        var standing = ResultStanding.Of(7m, []);

        Assert.Equal(new Standing(1, 100m, 1), standing);
    }

    [Fact]
    public void TheBestResult_IsFirst_AndAtTheHundredthPercentile()
    {
        var standing = ResultStanding.Of(10m, [1m, 2m, 3m]);

        Assert.Equal(1, standing.Rank);
        Assert.Equal(100m, standing.Percentile);
        Assert.Equal(4, standing.CohortSize);
    }

    [Fact]
    public void TheWorstResult_IsLast_AndAtItsOwnShareOfTheCohort()
    {
        // One of four scored this little or less (itself), so 25 per cent.
        var standing = ResultStanding.Of(1m, [2m, 3m, 4m]);

        Assert.Equal(4, standing.Rank);
        Assert.Equal(25m, standing.Percentile);
    }

    [Fact]
    public void CandidatesWithTheSameScore_ShareAPlace()
    {
        // Tied on 5 with another candidate: both are first, not one first and one second.
        var standing = ResultStanding.Of(5m, [5m, 1m]);

        Assert.Equal(1, standing.Rank);
    }

    [Fact]
    public void AfterATie_TheNextPlaceIsSkipped()
    {
        // Two candidates scored 5 and share first place, so a score of 3 is third, not second.
        var standing = ResultStanding.Of(3m, [5m, 5m, 1m]);

        Assert.Equal(3, standing.Rank);
    }

    [Fact]
    public void ATie_CountsInTheCandidatesFavour_ForThePercentile()
    {
        // Scored the same as one other candidate and above the third, so the two of them and the one below are at or below: 3 of 3.
        var standing = ResultStanding.Of(5m, [5m, 2m]);

        Assert.Equal(1, standing.Rank);
        Assert.Equal(100m, standing.Percentile);
    }

    [Fact]
    public void ThePercentile_IsRoundedDown_NotToTheNearestTwoDecimals()
    {
        // Two of three are at or below, which is 66.666..., and it is shown as 66.66, never rounded up to 66.67.
        var standing = ResultStanding.Of(3m, [1m, 5m]);

        Assert.Equal(66.66m, standing.Percentile);
    }

    [Fact]
    public void NegativeMarks_AreRankedLikeAnyOtherScore()
    {
        var standing = ResultStanding.Of(-3m, [-2m]);

        Assert.Equal(2, standing.Rank);
        Assert.Equal(50m, standing.Percentile);
    }
}
