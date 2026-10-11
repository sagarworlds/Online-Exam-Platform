namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>
/// The mean, median and distribution of an exam's candidate scores (FR-37), worked from each candidate's percentage of the marks on their paper.
/// Pure, so the figures are tested without a database and every screen that shows them agrees.
/// </summary>
/// <remarks>
/// Percentages rather than raw marks: candidates can sit papers with different questions, so their maxima differ, and six marks out of eight is
/// not the same result as six out of ten. FR-37 names a distribution without setting its bands, so the bands are a choice: deciles, ten bands
/// of ten percentage points. The width is one constant, so a different banding is a one-line change.
/// </remarks>
public static class ScoreDistribution
{
    /// <summary>The width of each band, in percentage points of the marks available.</summary>
    public const int BandWidth = 10;

    /// <summary>How many bands the distribution has: the scale from zero to one hundred percent in steps of <see cref="BandWidth"/>.</summary>
    public const int BandCount = 100 / BandWidth;

    /// <summary>Works out the mean, median and distribution of the candidates' percentages.</summary>
    /// <param name="percentages">Each counted candidate's score as a percentage of the marks on their paper, to two decimals.</param>
    /// <returns>
    /// The summary. Its mean and median are null, and every band is empty, when there are no percentages; the bands are always all present so a
    /// reader sees the whole scale.
    /// </returns>
    public static ScoreSummary Of(IReadOnlyCollection<decimal> percentages)
    {
        var sorted = percentages.OrderBy(p => p).ToList();
        var counts = new int[BandCount];
        foreach (var percentage in sorted)
            counts[BandIndexOf(percentage)]++;

        var bands = Enumerable.Range(0, BandCount)
            .Select(index => new ScoreBand(LowerOf(index), UpperOf(index), counts[index]))
            .ToList();

        return new ScoreSummary(sorted.Count, MeanOf(sorted), MedianOf(sorted), bands);
    }

    /// <summary>
    /// The band a percentage falls in. Bands are lower-inclusive, so a score of exactly ten percent is in the band above the one that stops at ten.
    /// </summary>
    /// <remarks>
    /// Negative marking can put a percentage below zero, and full marks land on one hundred; the bottom and top bands take those too, so every
    /// candidate is counted and none is dropped at the edges of the scale.
    /// </remarks>
    private static int BandIndexOf(decimal percentage) =>
        Math.Clamp((int)decimal.Floor(percentage / BandWidth), 0, BandCount - 1);

    private static decimal? LowerOf(int index) => index == 0 ? null : index * BandWidth;

    private static decimal? UpperOf(int index) => index == BandCount - 1 ? null : (index + 1) * BandWidth;

    private static decimal? MeanOf(IReadOnlyList<decimal> sorted) =>
        sorted.Count == 0 ? null : Round(sorted.Sum() / sorted.Count);

    /// <summary>
    /// The middle percentage, or the mean of the two middle ones for an even count. The median is used rather than the mean alone because a few
    /// very low or very high results can pull the mean away from where most candidates sit.
    /// </summary>
    private static decimal? MedianOf(IReadOnlyList<decimal> sorted)
    {
        if (sorted.Count == 0)
            return null;

        var middle = sorted.Count / 2;
        var median = sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        return Round(median);
    }

    /// <summary>Two decimals, rounded half away from zero: the rounding the percentages themselves use, so a figure never disagrees with them.</summary>
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
