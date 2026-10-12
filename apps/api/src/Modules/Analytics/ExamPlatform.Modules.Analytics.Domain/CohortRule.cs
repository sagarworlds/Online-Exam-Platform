namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>A counted result at one exam, reduced to what the cohort rule reads. The rule does not check release itself: the reader only gives released results.</summary>
/// <param name="CandidateId">The candidate who sat it.</param>
/// <param name="Score">The marks scored.</param>
/// <param name="MaxScore">The marks available on the paper the candidate sat.</param>
/// <param name="SubmittedAtUtc">When it was submitted; breaks a tie between a candidate's results with the same score.</param>
public readonly record struct CohortAttempt(Guid CandidateId, decimal Score, decimal MaxScore, DateTime SubmittedAtUtc);

/// <summary>Whether an exam's topper and average may be shown to the candidate (FR-36).</summary>
public enum CohortStatus
{
    /// <summary>The cohort met the minimum, so the topper and the average are worked out.</summary>
    Shown,

    /// <summary>The cohort is below the minimum, so neither the topper nor the average is worked out.</summary>
    BelowMinimum,
}

/// <summary>What the cohort rule gives for one exam (FR-36). The figures are null unless <see cref="Status"/> is <see cref="CohortStatus.Shown"/>.</summary>
/// <param name="Status">Whether the figures are shown, and if not, why.</param>
/// <param name="Size">How many candidates' results the figures were worked from; null while the cohort is below the minimum.</param>
/// <param name="TopperPercent">The highest percentage of marks in the cohort, to two decimals.</param>
/// <param name="AveragePercent">The mean percentage of marks in the cohort, to two decimals.</param>
public sealed record CohortFigures(CohortStatus Status, int? Size, decimal? TopperPercent, decimal? AveragePercent);

/// <summary>
/// The cohort a candidate's result at an exam is compared with (FR-36): who is in it, the minimum size before any figure is shown, and the topper
/// and average the figures are. Pure, so the rule is tested without a database and every comparison applies it the same way.
/// </summary>
/// <remarks>
/// The cohort is each candidate's best counted result at the exam. A candidate who sat an exam twice counts once, so a retake cannot give one
/// person two places in the comparison. The best is chosen by percent of marks, not raw score, because the comparison is made in percent: with
/// papers of different marks, the higher raw score is not necessarily the higher percent. FR-37 keeps its own raw-score rule, which is unchanged.
/// </remarks>
public static class CohortRule
{
    /// <summary>
    /// The candidate's best counted result: the highest percent of marks, then the higher raw score, then the later submission. Null when there is none.
    /// </summary>
    /// <param name="attempts">The candidate's counted results at one exam.</param>
    public static CohortAttempt? BestOf(IEnumerable<CohortAttempt> attempts)
    {
        var list = attempts.ToList();
        return list.Count == 0 ? null : Best(list);
    }

    /// <summary>
    /// Keeps each candidate's best counted result, so a candidate with several results at the exam is in the cohort once.
    /// </summary>
    /// <param name="counted">Every counted result at one exam, from any number of candidates.</param>
    public static IReadOnlyList<CohortAttempt> BestPerCandidate(IEnumerable<CohortAttempt> counted) =>
        counted.GroupBy(a => a.CandidateId).Select(group => Best(group.ToList())).ToList();

    /// <summary>The share of the marks a result earned, in percent to two decimals; null when its paper had no marks to earn.</summary>
    /// <param name="attempt">The result.</param>
    public static decimal? PercentOf(CohortAttempt attempt) => PerformanceMeasures.PercentOfMarks(attempt.Score, attempt.MaxScore);

    /// <summary>
    /// Works out the topper and the average of an exam's cohort, or nothing at all while the cohort is below <paramref name="minimumCohortSize"/>.
    /// </summary>
    /// <remarks>
    /// Why a cohort below the minimum shows no figures and no size: with two or three candidates the topper or the average is one person's result in
    /// disguise, and the size alone tells a candidate how few others sat. The status says the figures are held back, so the candidate sees a
    /// reason rather than a zero. The minimum is the item analysis threshold (FR-37), so the two rules cannot disagree about when a cohort is big enough.
    /// Why the size counts only candidates with a percentage: a candidate whose paper had no marks has no figure to top or to average, so they cannot
    /// be compared and do not make up the size the figures stand on.
    /// </remarks>
    /// <param name="counted">Every counted result at the exam, released results only, from any number of candidates.</param>
    /// <param name="minimumCohortSize">How many candidates must have a percentage before the topper and the average are shown.</param>
    public static CohortFigures Of(IEnumerable<CohortAttempt> counted, int minimumCohortSize)
    {
        var percents = BestPerCandidate(counted).Select(PercentOf).OfType<decimal>().ToList();
        if (percents.Count == 0 || percents.Count < minimumCohortSize)
            return new CohortFigures(CohortStatus.BelowMinimum, null, null, null);

        return new CohortFigures(CohortStatus.Shown, percents.Count, percents.Max(), Round(percents.Average()));
    }

    /// <remarks>
    /// Why a result with no marks ranks below every percentage: it has no percent to compare, so it must never displace a result that has one.
    /// Why the raw score breaks a tie in percent: two papers with the same percentage are equally strong, and the higher score is the one that
    /// earned more marks. Why the later submission breaks what is left: it is the candidate's most recent word on the exam.
    /// </remarks>
    private static CohortAttempt Best(IReadOnlyList<CohortAttempt> attempts) =>
        attempts
            .OrderByDescending(a => PercentOf(a) ?? decimal.MinValue)
            .ThenByDescending(a => a.Score)
            .ThenByDescending(a => a.SubmittedAtUtc)
            .First();

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
