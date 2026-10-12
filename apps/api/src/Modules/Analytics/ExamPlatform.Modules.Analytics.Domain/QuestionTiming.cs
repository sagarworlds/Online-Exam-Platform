namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>How long a candidate spent on questions, worked from the running totals the page reported (FR-36, time per question).</summary>
public static class QuestionTiming
{
    /// <summary>The average time per question, in seconds, to one decimal place.</summary>
    /// <remarks>
    /// Only questions the page reported on are passed in. A question with no report says nothing about how long was spent on it, so it is not
    /// counted as zero, which would pull the average down for a reason that has nothing to do with the candidate. The average is worked from the
    /// total rather than from each question's rounded time, so rounding cannot drift the figure over many questions.
    /// </remarks>
    /// <param name="seconds">The reported time of each question, in whole seconds.</param>
    /// <returns>The average, rounded half away from zero; null when no question was reported.</returns>
    public static decimal? AverageOf(IReadOnlyCollection<int> seconds) =>
        seconds.Count == 0
            ? null
            : Math.Round((decimal)seconds.Sum(s => (long)s) / seconds.Count, 1, MidpointRounding.AwayFromZero);
}
