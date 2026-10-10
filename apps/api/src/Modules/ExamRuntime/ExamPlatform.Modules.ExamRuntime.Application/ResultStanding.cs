namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>Where one result stands among the other candidates' results at the same exam (FR-32), as <see cref="ResultStanding.Of"/> works it out.</summary>
/// <param name="Rank">The place: 1 for the best. Candidates with the same score share a place.</param>
/// <param name="Percentile">
/// The share of the results compared, this one included, that scored the same or less, from 0 to 100, rounded down to two decimals.
/// </param>
/// <param name="CohortSize">How many results were compared, this one included.</param>
public readonly record struct Standing(int Rank, decimal Percentile, int CohortSize);

/// <summary>
/// Ranks and gives the percentile of a result among the other candidates' results at the same exam (FR-32, FR-35). Pure: the scores are
/// passed in, so the rules are tested without a database and the leaderboard uses the same ones.
/// </summary>
/// <remarks>
/// The cohort holds one score per other candidate: their best submitted attempt. Counting every attempt would let a candidate who retook
/// the exam take several places on the board, and a candidate's own attempts are never compared with each other.
/// Ranks use competition ranking: candidates with the same score share a place and the next place is skipped, so nobody is placed above
/// someone they tied with. The percentile counts ties in the candidate's favour, which is the usual reading of "scored at or above". It is
/// rounded down, so nobody is shown a percentile they did not quite earn.
/// </remarks>
public static class ResultStanding
{
    /// <summary>Places a result against the other candidates' best results.</summary>
    /// <param name="score">The result being placed.</param>
    /// <param name="othersBest">Each other candidate's best submitted score at the exam, once each; empty when no one else has finished.</param>
    /// <returns>The place, the percentile and how many results were compared.</returns>
    public static Standing Of(decimal score, IReadOnlyCollection<decimal> othersBest)
    {
        var higher = othersBest.Count(other => other > score);
        // Counts this result itself too, which is why one is added: it is at or below its own score.
        var atOrBelow = othersBest.Count(other => other <= score) + 1;
        var cohortSize = othersBest.Count + 1;
        // Scaled to hundredths of a percent before flooring, so the two decimals survive the rounding down.
        var percentile = Math.Floor(atOrBelow * 10_000m / cohortSize) / 100m;
        return new Standing(higher + 1, percentile, cohortSize);
    }
}
