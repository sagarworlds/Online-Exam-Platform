namespace ExamPlatform.Modules.ExamRuntime.Contracts;

/// <summary>
/// One released result of a candidate, as another module sees it: the score and how the answers fell in each section. It carries no
/// question text, options or answer key.
/// </summary>
/// <param name="AttemptId">The attempt the result belongs to.</param>
/// <param name="ExamId">The exam the attempt was at.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="SubmittedAtUtc">When the attempt was submitted.</param>
/// <param name="Score">The marks scored, as the latest version of the result says.</param>
/// <param name="MaxScore">The marks available.</param>
/// <param name="Sections">How the answers fell in each section, in exam order.</param>
public sealed record CandidateResult(
    Guid AttemptId,
    Guid ExamId,
    string ExamName,
    DateTime SubmittedAtUtc,
    decimal Score,
    decimal MaxScore,
    IReadOnlyList<CandidateSectionResult> Sections);

/// <summary>How a candidate's answers fell in one section of an exam.</summary>
/// <param name="SectionId">The section's id.</param>
/// <param name="Name">The section's name, as the exam's author wrote it.</param>
/// <param name="Score">The marks the section's questions earned; may be negative.</param>
/// <param name="CorrectCount">How many of its questions were answered fully correctly.</param>
/// <param name="WrongCount">How many were answered wrongly.</param>
/// <param name="PartialCount">How many multiple-answer questions were answered partly right.</param>
/// <param name="UnansweredCount">How many were left unanswered.</param>
public sealed record CandidateSectionResult(
    Guid SectionId,
    string Name,
    decimal Score,
    int CorrectCount,
    int WrongCount,
    int PartialCount,
    int UnansweredCount);
