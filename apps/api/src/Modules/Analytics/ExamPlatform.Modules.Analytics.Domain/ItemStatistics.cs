namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>
/// The difficulty and discrimination indices of an exam's questions (FR-37), as classical test theory defines them. Pure, so the figures are
/// tested without a database and every screen and export that shows them agrees.
/// </summary>
public static class ItemStatistics
{
    /// <summary>
    /// The share of the cohort in each of the upper and lower groups: the top and bottom 27%, the conventional split for discrimination.
    /// </summary>
    /// <remarks>
    /// Kelley's 27% rule: it is the split that gives the most reliable contrast between the two extreme groups. Wider groups blur the contrast
    /// with middling candidates; narrower ones let one candidate's answer decide the index.
    /// </remarks>
    public const decimal GroupShare = 0.27m;

    /// <summary>How many candidates each group holds for a cohort of this size, at least one once there are two candidates to compare.</summary>
    /// <param name="cohortSize">How many counted candidates the exam has.</param>
    /// <returns>The group size; zero when there are fewer than two candidates, since nothing can be compared.</returns>
    public static int GroupSize(int cohortSize)
    {
        if (cohortSize < 2)
            return 0;

        return Math.Max(1, (int)decimal.Round(cohortSize * GroupShare, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Splits the cohort into the upper and lower groups by score.</summary>
    /// <remarks>
    /// Candidates with the same score at a group's edge are ordered by id, so the split is arbitrary but the same on every run: a figure that
    /// changed each time it was read would not be a measure. The group size never exceeds half the cohort, so the two groups cannot overlap.
    /// </remarks>
    /// <param name="cohort">Each counted candidate's best score.</param>
    /// <returns>The two groups; both are empty when the cohort has fewer than two candidates.</returns>
    public static CohortGroups GroupsOf(IReadOnlyCollection<CandidateScore> cohort)
    {
        var ranked = cohort.OrderByDescending(c => c.Score).ThenBy(c => c.CandidateId).ToList();
        var size = GroupSize(ranked.Count);
        if (size == 0)
            return new CohortGroups(new HashSet<Guid>(), new HashSet<Guid>());

        var upper = ranked.Take(size).Select(c => c.CandidateId).ToHashSet();
        var lower = ranked.Skip(ranked.Count - size).Select(c => c.CandidateId).ToHashSet();
        return new CohortGroups(upper, lower);
    }

    /// <summary>
    /// Works out the indices of one question from its answers.
    /// </summary>
    /// <remarks>
    /// Difficulty is the share of candidates who had the question and answered it fully correctly. An unanswered question counts as wrong: a
    /// candidate who could not answer it did not show they could, so leaving it out would make the question look easier than it was.
    /// Discrimination is the upper group's share correct less the lower group's, over those in each group who had the question. A question
    /// that most of the top group got right and the bottom group got wrong separates them well; one that both groups answer alike does not.
    /// Below the threshold both are withheld: an index drawn from one or two candidates' answers would look like a measurement.
    /// </remarks>
    /// <param name="groups">The cohort's upper and lower groups.</param>
    /// <param name="answers">Each answer to the question by a counted candidate who had it on their paper.</param>
    /// <param name="minimumCohortSize">How many candidates must have had the question before its indices are shown.</param>
    /// <returns>The indices of the question.</returns>
    public static ItemIndices IndicesOf(CohortGroups groups, IReadOnlyCollection<ItemAnswer> answers, int minimumCohortSize)
    {
        var attempts = answers.Count;
        var correct = answers.Count(a => a.Correct);
        if (attempts < minimumCohortSize)
            return new ItemIndices(attempts, correct, Difficulty: null, Discrimination: null);

        return new ItemIndices(
            attempts,
            correct,
            Round(Share(correct, attempts)),
            DiscriminationOf(groups, answers));
    }

    private static decimal? DiscriminationOf(CohortGroups groups, IReadOnlyCollection<ItemAnswer> answers)
    {
        var upper = answers.Where(a => groups.Upper.Contains(a.CandidateId)).ToList();
        var lower = answers.Where(a => groups.Lower.Contains(a.CandidateId)).ToList();

        // With no one from a group on the paper there is nothing to compare against, so no index is better than a made-up one.
        if (upper.Count == 0 || lower.Count == 0)
            return null;

        return Round(Share(upper.Count(a => a.Correct), upper.Count) - Share(lower.Count(a => a.Correct), lower.Count));
    }

    private static decimal Share(int correct, int total) => (decimal)correct / total;

    /// <summary>Four decimals: the indices are shown to two places as a percentage or a signed figure, and the rest is rounding noise.</summary>
    private static decimal Round(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
