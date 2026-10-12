using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// The minimum cohort rule for a question's average time (FR-37): the average is shown only once enough counted candidates have a time for the
/// question, against the same threshold the difficulty and discrimination indices use.
/// </summary>
/// <remarks>
/// A question nobody was timed on has no average, and it is reported as having no times, not as zero: a zero would pull the figure down for a
/// reason that has nothing to do with the candidates' pace. A handful of timed answers is withheld rather than averaged, because one or two
/// candidates' pace would read as a measure of the question.
/// </remarks>
public static class TimedCohortRule
{
    /// <summary>The time figure of one question, from the reported times of the counted candidates who have one for it.</summary>
    /// <param name="seconds">The reported time of each candidate who has one for the question, in whole seconds.</param>
    /// <param name="minimumCohortSize">How many candidates must have a time for the question before its average is shown.</param>
    /// <returns>How many candidates were timed, the average when it is shown, and the status that says whether and why it is shown.</returns>
    public static ItemTimeDto FigureOf(IReadOnlyCollection<int> seconds, int minimumCohortSize)
    {
        if (seconds.Count == 0)
            return new ItemTimeDto(0, null, ItemTimeStatus.NoTimes);

        if (seconds.Count < minimumCohortSize)
            return new ItemTimeDto(seconds.Count, null, ItemTimeStatus.BelowMinimumCohort);

        return new ItemTimeDto(seconds.Count, QuestionTiming.AverageOf(seconds), ItemTimeStatus.Shown);
    }
}
