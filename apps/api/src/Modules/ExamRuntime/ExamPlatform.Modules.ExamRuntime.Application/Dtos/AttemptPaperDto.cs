using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>One question on a candidate's paper, as staff see it.</summary>
/// <param name="Id">The question's id in the question bank.</param>
/// <param name="Text">The question text as sanitized HTML, or null if the bank no longer has the question.</param>
/// <param name="Drawn">Whether a draw rule picked it for this candidate, as opposed to being one of the exam's fixed questions.</param>
/// <param name="Options">The options in the order this candidate saw them, each marking the correct one and whether the candidate chose it; null if the bank no longer has the question.</param>
/// <param name="Verdict">Whether the answer was correct, partly correct, wrong or missing; null until the attempt is submitted.</param>
/// <param name="Marks">The marks the answer earned, which may be negative; null until the attempt is submitted.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct.</param>
public sealed record AttemptPaperQuestionDto(
    Guid Id, string? Text, bool Drawn, IReadOnlyList<ReviewOptionDto>? Options = null, AnswerVerdict? Verdict = null, decimal? Marks = null,
    bool AllowsMultiple = false);

/// <summary>A section of a candidate's paper.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Questions">The section's questions in the order this candidate saw them.</param>
public sealed record AttemptPaperSectionDto(Guid Id, string Name, IReadOnlyList<AttemptPaperQuestionDto> Questions);

/// <summary>The questions one attempt consisted of, so staff can see what a candidate was actually asked.</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="Number">Which attempt this is for the candidate, from 1.</param>
/// <param name="HasDrawnQuestions">Whether any question was drawn for this candidate, so papers of the same exam can differ.</param>
/// <param name="Sections">The sections with their questions.</param>
/// <param name="Status">Whether the candidate is still working or the attempt is submitted.</param>
/// <param name="Score">The marks scored; null until the attempt is submitted.</param>
/// <param name="MaxScore">The marks available; null until the attempt is submitted.</param>
/// <param name="IsInvalidated">Whether staff invalidated the attempt (FR-29); the candidate is not shown its score, staff still see what it was.</param>
public sealed record AttemptPaperDto(
    Guid AttemptId, int Number, bool HasDrawnQuestions, IReadOnlyList<AttemptPaperSectionDto> Sections,
    AttemptStatus Status = AttemptStatus.InProgress, decimal? Score = null, decimal? MaxScore = null, bool IsInvalidated = false);
