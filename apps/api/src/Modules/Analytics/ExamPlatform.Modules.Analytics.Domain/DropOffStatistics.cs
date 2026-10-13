namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>
/// Where candidates stop an exam (FR-37 drop-off points). A candidate drops off at the last question they answered when the clock ended their
/// attempt with later questions still unanswered. It is pure, so the rule is checked without a database or a clock.
/// </summary>
/// <remarks>
/// Answers are the only evidence of progress. A question the candidate merely opened says nothing, and a question-time report can be withheld
/// (for minors, by default), so a time is not a reliable mark of having reached a question. A candidate who answered nothing gives no point at
/// all, because nothing says how far they got. A candidate who submitted the paper themselves did not drop off, however few they answered.
/// </remarks>
public static class DropOffStatistics
{
    /// <summary>The rounding every share in the item analysis uses, to four decimal places.</summary>
    private const int Decimals = 4;

    /// <summary>
    /// How many candidates reached each question, and how many of them stopped there. A question no candidate reached has no entry.
    /// </summary>
    /// <param name="papers">Each counted candidate's paper, once each: the caller keeps one attempt per candidate.</param>
    public static IReadOnlyDictionary<Guid, DropOffCount> Of(IEnumerable<CandidatePaper> papers)
    {
        var reached = new Dictionary<Guid, int>();
        var droppedOff = new Dictionary<Guid, int>();

        foreach (var paper in papers)
        {
            var last = LastAnsweredIndex(paper.Questions);
            if (last < 0)
                continue;

            // Answering question i means the candidate got as far as i, so every question up to it was reached, answered or not.
            for (var i = 0; i <= last; i++)
                Increment(reached, paper.Questions[i].QuestionId);

            // Only the clock ends a paper with questions left, so a candidate who submitted the rest themselves did not stop there.
            var stoppedWithQuestionsLeft = last < paper.Questions.Count - 1;
            if (paper.EndedByClock && stoppedWithQuestionsLeft)
                Increment(droppedOff, paper.Questions[last].QuestionId);
        }

        return reached.ToDictionary(
            pair => pair.Key,
            pair => new DropOffCount(pair.Value, droppedOff.GetValueOrDefault(pair.Key)));
    }

    /// <summary>
    /// The share of the candidates who reached a question and stopped there, from 0 to 1. It is null while fewer than
    /// <paramref name="minimumCohortSize"/> candidates reached the question, since a handful of candidates can swing the share.
    /// </summary>
    /// <param name="count">How many reached the question and how many of them stopped there.</param>
    /// <param name="minimumCohortSize">How many candidates must have reached the question before its share is shown.</param>
    public static decimal? RateOf(DropOffCount count, int minimumCohortSize)
    {
        if (count.Reached == 0 || count.Reached < minimumCohortSize)
            return null;

        return decimal.Round((decimal)count.DroppedOff / count.Reached, Decimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>The index of the last answered question, or -1 when the candidate answered none.</summary>
    private static int LastAnsweredIndex(IReadOnlyList<PaperQuestion> questions)
    {
        for (var i = questions.Count - 1; i >= 0; i--)
        {
            if (questions[i].Answered)
                return i;
        }

        return -1;
    }

    private static void Increment(Dictionary<Guid, int> counts, Guid questionId) =>
        counts[questionId] = counts.GetValueOrDefault(questionId) + 1;
}

/// <summary>
/// One counted candidate's paper, reduced to what drop-off reads: its questions in the order the paper gives them, and whether the clock
/// ended the attempt rather than the candidate submitting it (FR-37).
/// </summary>
/// <param name="EndedByClock">Whether time ran out before the candidate submitted, so the attempt was closed for them.</param>
/// <param name="Questions">The paper's questions in order, each with whether the candidate answered it.</param>
public sealed record CandidatePaper(bool EndedByClock, IReadOnlyList<PaperQuestion> Questions);

/// <summary>One question on a candidate's paper, and whether the candidate answered it (FR-37).</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="Answered">Whether the candidate left an answer to it.</param>
public sealed record PaperQuestion(Guid QuestionId, bool Answered);

/// <summary>How far candidates got through one question, and how many of them stopped there (FR-37).</summary>
/// <param name="Reached">How many candidates got as far as the question: they answered it, or a question after it.</param>
/// <param name="DroppedOff">How many of them stopped here: the clock ended their attempt with later questions still unanswered.</param>
public sealed record DropOffCount(int Reached, int DroppedOff);
