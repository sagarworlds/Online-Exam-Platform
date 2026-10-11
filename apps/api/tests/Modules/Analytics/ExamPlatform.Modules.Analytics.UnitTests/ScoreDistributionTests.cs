using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The mean, median and distribution of an exam's candidate scores, as percentages of their papers' marks (FR-37).</summary>
public class ScoreDistributionTests
{
    private static int[] CountsOf(ScoreSummary summary) => summary.Bands.Select(band => band.Count).ToArray();

    [Fact]
    public void WithNoCandidates_TheMeanAndMedianAreNull_AndEveryBandIsShownEmpty()
    {
        var summary = ScoreDistribution.Of([]);

        Assert.Equal(0, summary.Count);
        Assert.Null(summary.Mean);
        Assert.Null(summary.Median);
        Assert.Equal(ScoreDistribution.BandCount, summary.Bands.Count);
        Assert.All(summary.Bands, band => Assert.Equal(0, band.Count));
    }

    [Fact]
    public void TheMean_IsTheAverage_RoundedToTwoDecimals()
    {
        // The total of 5 over three candidates is 1.666..., which the figure shows as 1.67.
        Assert.Equal(1.67m, ScoreDistribution.Of([1m, 2m, 2m]).Mean);
    }

    [Fact]
    public void TheMedian_OfAnOddCount_IsTheMiddleCandidate_NotTheMean()
    {
        // The mean of 10, 20 and 90 is 40, but the middle candidate is at 20: one very high result must not pull the median up.
        var summary = ScoreDistribution.Of([90m, 10m, 20m]);

        Assert.Equal(20m, summary.Median);
        Assert.Equal(40m, summary.Mean);
    }

    [Fact]
    public void TheMedian_OfAnEvenCount_IsTheMeanOfTheTwoMiddleCandidates()
    {
        Assert.Equal(25m, ScoreDistribution.Of([40m, 10m, 30m, 20m]).Median);
    }

    [Fact]
    public void AScoreOnABoundary_FallsInTheBandAbove()
    {
        // Bands are lower-inclusive: ten percent is in the band that starts at ten, not the one that stops there.
        var counts = CountsOf(ScoreDistribution.Of([9.99m, 10m, 89.99m, 90m]));

        Assert.Equal(new[] { 1, 1, 0, 0, 0, 0, 0, 0, 1, 1 }, counts);
    }

    [Fact]
    public void NegativeMarking_PutsAScoreBelowZero_InTheBottomBand_AndCountsIt()
    {
        // Under negative marking a candidate can score below nothing; that is a real result, so it is counted rather than dropped.
        var summary = ScoreDistribution.Of([-12.5m, 0m]);

        Assert.Equal(2, summary.Bands[0].Count);
        Assert.Equal(2, summary.Count);
    }

    [Fact]
    public void TheTopBand_TakesFullMarks_AndAnythingAboveThem()
    {
        var counts = CountsOf(ScoreDistribution.Of([100m, 105m]));

        Assert.Equal(2, counts[ScoreDistribution.BandCount - 1]);
    }

    [Fact]
    public void EveryCandidate_IsInExactlyOneBand()
    {
        var percentages = new[] { 0m, 3.5m, 12m, 47.25m, 50m, 66.67m, 89.99m, 90m, 100m, -4m };

        var summary = ScoreDistribution.Of(percentages);

        Assert.Equal(percentages.Length, summary.Count);
        Assert.Equal(percentages.Length, summary.Bands.Sum(band => band.Count));
    }

    [Fact]
    public void TheBands_CoverTheScaleInTens_WithOpenEndsAtBothExtremes()
    {
        var bands = ScoreDistribution.Of([]).Bands;

        Assert.Null(bands[0].From);
        Assert.Equal(10m, bands[0].To);
        Assert.Equal(10m, bands[1].From);
        Assert.Equal(20m, bands[1].To);
        Assert.Equal(90m, bands[^1].From);
        Assert.Null(bands[^1].To);
    }
}
