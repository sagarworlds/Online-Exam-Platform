namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>One question on a candidate's paper, as staff see it.</summary>
/// <param name="Id">The question's id in the question bank.</param>
/// <param name="Text">The question text as sanitized HTML, or null if the bank no longer has the question.</param>
/// <param name="Drawn">Whether a draw rule picked it for this candidate, as opposed to being one of the exam's fixed questions.</param>
public sealed record AttemptPaperQuestionDto(Guid Id, string? Text, bool Drawn);

/// <summary>A section of a candidate's paper.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Questions">The section's questions in the authored order, before any shuffling the candidate saw.</param>
public sealed record AttemptPaperSectionDto(Guid Id, string Name, IReadOnlyList<AttemptPaperQuestionDto> Questions);

/// <summary>The questions one attempt consisted of, so staff can see what a candidate was actually asked.</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="Number">Which attempt this is for the candidate, from 1.</param>
/// <param name="HasDrawnQuestions">Whether any question was drawn for this candidate, so papers of the same exam can differ.</param>
/// <param name="Sections">The sections with their questions.</param>
public sealed record AttemptPaperDto(Guid AttemptId, int Number, bool HasDrawnQuestions, IReadOnlyList<AttemptPaperSectionDto> Sections);
