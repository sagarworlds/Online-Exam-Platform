namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>One candidate's place on a board.</summary>
/// <param name="CandidateId">The candidate.</param>
/// <param name="Score">The score on this board.</param>
/// <param name="Rank">The place: 1 for the best. Candidates with the same score share a place.</param>
public readonly record struct BoardRow(Guid CandidateId, decimal Score, int Rank);

/// <summary>
/// Orders and ranks the candidates of a board (FR-35). Pure: the scores are passed in, so the overall, batch and subject boards share one rule.
/// </summary>
/// <remarks>
/// Competition ranking, as the candidate's result page uses: candidates with the same score share a place and the next place is skipped, so
/// nobody is placed above someone they tied with. Ties are listed in candidate id order, so the same board always reads the same way.
/// </remarks>
public static class LeaderboardRanking
{
    /// <summary>
    /// The most rows a board lists. A board of a large exam is capped so a response stays small; the candidate's own row is added separately
    /// when it falls outside the list.
    /// </summary>
    public const int MaxEntries = 100;

    /// <summary>Ranks the scores, best first.</summary>
    /// <param name="results">Each candidate's score on the board, once each.</param>
    /// <returns>Every candidate with the place they hold, best first.</returns>
    public static IReadOnlyList<BoardRow> Rank(IEnumerable<(Guid CandidateId, decimal Score)> results)
    {
        var ordered = results.OrderByDescending(r => r.Score).ThenBy(r => r.CandidateId).ToList();
        var rows = new List<BoardRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            // A tie takes the place of the candidate above; anyone below is counted by position, so the next place is skipped.
            var rank = i > 0 && ordered[i].Score == ordered[i - 1].Score ? rows[i - 1].Rank : i + 1;
            rows.Add(new BoardRow(ordered[i].CandidateId, ordered[i].Score, rank));
        }

        return rows;
    }
}
