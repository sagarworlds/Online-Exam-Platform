namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>
/// The two measures analytics shows a candidate (FR-36): how much of the available marks a result earned, and how accurate the candidate
/// was on the questions they answered. Pure, so every screen that shows a figure computes it the same way.
/// </summary>
public static class PerformanceMeasures
{
    /// <summary>
    /// The share of the available marks a result earned, in percent to two decimals; null when no marks were available to earn.
    /// </summary>
    /// <remarks>
    /// Not clamped at zero: with negative marking a result can earn less than nothing, and showing that as zero would hide a bad sitting.
    /// Rounded half away from zero, the rounding the marks themselves use, so a figure never disagrees with the marks it is worked from.
    /// </remarks>
    /// <param name="score">The marks scored.</param>
    /// <param name="maxScore">The marks available.</param>
    /// <returns>The percentage, or null when <paramref name="maxScore"/> is not positive.</returns>
    public static decimal? PercentOfMarks(decimal score, decimal maxScore) =>
        maxScore <= 0 ? null : decimal.Round(score * 100m / maxScore, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The share of answered questions that were fully correct, in percent to two decimals; null when nothing was answered.
    /// </summary>
    /// <remarks>
    /// Skipped questions are left out of the denominator. A candidate who skips a question has shown nothing about it, so skipping must not
    /// lower their accuracy the way a wrong answer does. A partly right answer counts as answered but not as correct, because the candidate
    /// did not give the full set of correct options.
    /// </remarks>
    /// <param name="correct">Questions answered fully correctly.</param>
    /// <param name="wrong">Questions answered wrongly.</param>
    /// <param name="partial">Multiple-answer questions answered partly right.</param>
    /// <returns>The accuracy, or null when no question was answered.</returns>
    public static decimal? Accuracy(int correct, int wrong, int partial)
    {
        var answered = correct + wrong + partial;
        return answered == 0 ? null : decimal.Round(correct * 100m / answered, 2, MidpointRounding.AwayFromZero);
    }
}
