namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>A candidate's performance across the exams they sat, from their released results (FR-36).</summary>
/// <param name="ResultCount">How many released results the figures are worked from.</param>
/// <param name="Trend">One point per released result, oldest submission first, for the score trend.</param>
/// <param name="Sections">How the candidate's answers fell in each section name across the exams, weakest accuracy first.</param>
public sealed record CandidateAnalyticsDto(
    int ResultCount,
    IReadOnlyList<ScorePointDto> Trend,
    IReadOnlyList<SectionPerformanceDto> Sections);

/// <summary>One released result on the score trend.</summary>
/// <param name="AttemptId">The attempt the point is for.</param>
/// <param name="ExamId">The exam the attempt was at.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="SubmittedAtUtc">When the attempt was submitted; the point's place on the trend.</param>
/// <param name="Score">The marks scored.</param>
/// <param name="MaxScore">The marks available.</param>
/// <param name="PercentOfMarks">The score as a share of the marks available, in percent; null when no marks were available.</param>
public sealed record ScorePointDto(
    Guid AttemptId,
    Guid ExamId,
    string ExamName,
    DateTime SubmittedAtUtc,
    decimal Score,
    decimal MaxScore,
    decimal? PercentOfMarks);

/// <summary>How a candidate's answers fell in one section name, added up across every released result that had it.</summary>
/// <param name="Name">The section's name, as the exam's author wrote it.</param>
/// <param name="ResultCount">How many released results included a section of this name.</param>
/// <param name="CorrectCount">Questions answered fully correctly across them.</param>
/// <param name="WrongCount">Questions answered wrongly across them.</param>
/// <param name="PartialCount">Multiple-answer questions answered partly right across them.</param>
/// <param name="UnansweredCount">Questions left unanswered across them.</param>
/// <param name="Accuracy">The share of answered questions that were fully correct, in percent; null when nothing was answered.</param>
public sealed record SectionPerformanceDto(
    string Name,
    int ResultCount,
    int CorrectCount,
    int WrongCount,
    int PartialCount,
    int UnansweredCount,
    decimal? Accuracy);
