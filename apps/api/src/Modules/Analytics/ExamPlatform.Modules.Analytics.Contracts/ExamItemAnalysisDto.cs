namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>An exam's item analysis: the questions in order, with their indices and the settings they were worked out under (FR-37).</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="ResultsReleased">Whether the author has released the results. While false, no attempt counts and the questions list is empty.</param>
/// <param name="CandidateCount">How many candidates' released results the indices were worked from, each counted once by their best result.</param>
/// <param name="MinimumCohortSize">How many candidates must have had a question before its indices are shown.</param>
/// <param name="GroupSize">How many candidates are in each of the upper and lower groups behind the discrimination index.</param>
/// <param name="Questions">The questions in the exam's order, with any drawn questions after them.</param>
public sealed record ExamItemAnalysisDto(
    Guid ExamId,
    string ExamName,
    bool ResultsReleased,
    int CandidateCount,
    int MinimumCohortSize,
    int GroupSize,
    IReadOnlyList<ItemRowDto> Questions);

/// <summary>One question's row in the item analysis.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="Position">Where the question sits in the exam, from 1.</param>
/// <param name="Text">A plain-text preview of the question, shortened for a table. Never the answer key or the options.</param>
/// <param name="Attempts">How many counted candidates had the question on their paper.</param>
/// <param name="CorrectCount">How many of them answered it fully correctly.</param>
/// <param name="Difficulty">The share of them who answered correctly, from 0 to 1; null while below the minimum cohort size.</param>
/// <param name="Discrimination">The upper group's share correct less the lower group's, from -1 to 1; null while below the minimum, or when a group has no one to compare.</param>
public sealed record ItemRowDto(
    Guid QuestionId,
    int Position,
    string Text,
    int Attempts,
    int CorrectCount,
    decimal? Difficulty,
    decimal? Discrimination);
