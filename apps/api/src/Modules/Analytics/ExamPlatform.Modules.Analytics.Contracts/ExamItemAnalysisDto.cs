namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>An exam's item analysis: the questions in order, with their indices and the settings they were worked out under (FR-37).</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="ResultsReleased">Whether the author has released the results. While false, no attempt counts and the questions list is empty.</param>
/// <param name="CandidateCount">How many candidates' released results the indices were worked from, each counted once by their best result.</param>
/// <param name="MinimumCohortSize">How many candidates must have had a question before its indices are shown.</param>
/// <param name="GroupSize">How many candidates are in each of the upper and lower groups behind the discrimination index.</param>
/// <param name="Questions">The questions in the exam's order, with any drawn questions after them.</param>
/// <param name="Scores">The candidates' mean, median and score distribution (FR-37), with no figures while the results are held.</param>
/// <param name="Time">The average time per question across the counted candidates' timed answers (FR-37), with no figures while the results are held.</param>
public sealed record ExamItemAnalysisDto(
    Guid ExamId,
    string ExamName,
    bool ResultsReleased,
    int CandidateCount,
    int MinimumCohortSize,
    int GroupSize,
    IReadOnlyList<ItemRowDto> Questions,
    ExamScoreSummaryDto Scores,
    ExamTimeSummaryDto Time);

/// <summary>One question's row in the item analysis.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="Position">Where the question sits in the exam, from 1.</param>
/// <param name="Text">A plain-text preview of the question, shortened for a table. Never the answer key or the options.</param>
/// <param name="Attempts">How many counted candidates had the question on their paper.</param>
/// <param name="CorrectCount">How many of them answered it fully correctly.</param>
/// <param name="Difficulty">The share of them who answered correctly, from 0 to 1; null while below the minimum cohort size.</param>
/// <param name="Discrimination">The upper group's share correct less the lower group's, from -1 to 1; null while below the minimum, or when a group has no one to compare.</param>
/// <param name="Time">How long the counted candidates spent on the question, and whether its average is shown.</param>
/// <param name="DropOff">How far the counted candidates got to this question, and how many of them stopped at it (FR-37).</param>
public sealed record ItemRowDto(
    Guid QuestionId,
    int Position,
    string Text,
    int Attempts,
    int CorrectCount,
    decimal? Difficulty,
    decimal? Discrimination,
    ItemTimeDto Time,
    ItemDropOffDto DropOff);

/// <summary>How far the counted candidates got to one question, and where they stopped (FR-37 drop-off points).</summary>
/// <param name="Reached">How many counted candidates got as far as the question: they answered it or a question after it. A candidate who answered nothing is not counted.</param>
/// <param name="DroppedOff">How many of them stopped at this question: the clock ended their attempt with later questions still unanswered.</param>
/// <param name="Rate">The share of those who reached it and stopped here, from 0 to 1; null while fewer than the minimum cohort size reached it.</param>
public sealed record ItemDropOffDto(int Reached, int DroppedOff, decimal? Rate);

/// <summary>How long the counted candidates spent on one question, and whether its average is shown (FR-37).</summary>
/// <param name="CandidatesTimed">How many counted candidates have a reported time for the question. A candidate with no reported time is not counted as zero.</param>
/// <param name="AverageSeconds">The average time in seconds, to one decimal place; null unless <paramref name="Status"/> is Shown.</param>
/// <param name="Status">Whether the average is shown and, if not, why, so a withheld average is never read as zero.</param>
public sealed record ItemTimeDto(int CandidatesTimed, decimal? AverageSeconds, ItemTimeStatus Status);

/// <summary>Why a question's average time is shown or withheld (FR-37).</summary>
public enum ItemTimeStatus
{
    /// <summary>At least the minimum cohort size of counted candidates have a time for the question, so its average is shown.</summary>
    Shown,

    /// <summary>Some candidates have a time for the question, but fewer than the minimum cohort size, so the average is withheld.</summary>
    BelowMinimumCohort,

    /// <summary>No counted candidate has a reported time for the question, so there is no average to show.</summary>
    NoTimes,
}

/// <summary>The average time per question across an exam's counted candidates (FR-37).</summary>
/// <param name="QuestionsTimed">How many question answers across the counted candidates have a reported time.</param>
/// <param name="AverageSeconds">The average over those timed answers, in seconds, to one decimal place; null when none was timed.</param>
public sealed record ExamTimeSummaryDto(int QuestionsTimed, decimal? AverageSeconds);
