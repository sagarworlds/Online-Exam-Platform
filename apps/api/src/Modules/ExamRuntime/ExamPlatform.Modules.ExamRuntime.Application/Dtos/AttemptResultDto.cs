namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>
/// One section of a result: the marks it earned and how its questions were answered. Carries no question text, options or answer key,
/// so the result can be read without the answers (those are the review's, once released).
/// </summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Score">The marks the section's questions earned; may be negative.</param>
/// <param name="CorrectCount">How many of its questions were answered correctly.</param>
/// <param name="WrongCount">How many were answered wrongly.</param>
/// <param name="PartialCount">How many multiple-answer questions were answered partly right.</param>
/// <param name="UnansweredCount">How many were left unanswered.</param>
public sealed record SectionResultDto(
    Guid Id, string Name, decimal Score, int CorrectCount, int WrongCount, int PartialCount, int UnansweredCount);

/// <summary>
/// A submitted attempt's result (FR-32): the score, where it stands among the exam's released results, and the marks by section. Only
/// built once the exam's author has released the result.
/// </summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="Number">Which attempt this is for the candidate at the exam, from 1.</param>
/// <param name="SubmittedAtUtc">When the attempt ended.</param>
/// <param name="AutoSubmitted">Whether it ended because time ran out.</param>
/// <param name="Score">The marks scored, as the latest version of the result says.</param>
/// <param name="MaxScore">The marks available.</param>
/// <param name="CorrectCount">How many questions were answered correctly.</param>
/// <param name="WrongCount">How many were answered wrongly.</param>
/// <param name="PartialCount">How many multiple-answer questions were answered partly right.</param>
/// <param name="UnansweredCount">How many were left unanswered.</param>
/// <param name="Sections">The marks by section, in exam order.</param>
/// <param name="Rank">The place among the exam's released results; 1 is the best. Ties share a place.</param>
/// <param name="Percentile">The share of the results compared that scored the same or less, from 0 to 100, rounded down to two decimals.</param>
/// <param name="CohortSize">How many results the rank and percentile were worked out from, this one included.</param>
/// <param name="Provisional">
/// Whether the exam's window is still open, so other candidates may still submit and move the rank. Once it closes the rank is final.
/// </param>
/// <param name="ResultVersion">Which version of the result the score above is: 1 as first submitted, one more for each revision (FR-31).</param>
public sealed record AttemptResultDto(
    Guid AttemptId,
    Guid ExamId,
    string ExamName,
    int Number,
    DateTime? SubmittedAtUtc,
    bool AutoSubmitted,
    decimal Score,
    decimal MaxScore,
    int CorrectCount,
    int WrongCount,
    int PartialCount,
    int UnansweredCount,
    IReadOnlyList<SectionResultDto> Sections,
    int Rank,
    decimal Percentile,
    int CohortSize,
    bool Provisional,
    int ResultVersion);
