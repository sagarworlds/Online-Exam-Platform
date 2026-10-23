using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Where the AI evaluator stands on one typed answer.</summary>
public enum AiCheckState
{
    /// <summary>The evaluator has not judged it: it was not set up when the attempt ended, or it was never asked.</summary>
    NotChecked,

    /// <summary>It is queued, or being asked about, now.</summary>
    Waiting,

    /// <summary>The evaluator gave a verdict, which counts when the typed answer matches no accepted answer word for word.</summary>
    Judged,

    /// <summary>The platform stopped waiting for the evaluator and marked the answer by the accepted answers alone.</summary>
    GaveUp,
}

/// <summary>What staff are shown about the AI evaluator's part in one typed answer, beside the paper.</summary>
/// <param name="State">Where the evaluator stands on it.</param>
/// <param name="Verdict">The evaluator's verdict, right or wrong; null before it has given one. While the answer is waiting again, the earlier verdict.</param>
/// <param name="Reason">The evaluator's one-sentence reason, or, for <see cref="AiCheckState.GaveUp"/>, why none was given. For staff only.</param>
/// <param name="Model">The model that gave the verdict.</param>
/// <param name="AtUtc">When the verdict was given, or when the platform stopped waiting.</param>
/// <param name="Failures">How many times asking failed since the answer was queued; more than 0 while waiting means the evaluator is having trouble.</param>
/// <param name="LastError">What went wrong the last time, in a few words and never with the answer in it.</param>
public sealed record AttemptPaperAiCheckDto(
    AiCheckState State, TypedAnswerVerdict? Verdict, string? Reason, string? Model, DateTime? AtUtc, int Failures, string? LastError);

/// <summary>One question on a candidate's paper, as staff see it.</summary>
/// <param name="Id">The question's id in the question bank.</param>
/// <param name="Text">The question text as sanitized HTML, or null if the bank no longer has the question.</param>
/// <param name="Drawn">Whether a draw rule picked it for this candidate, as opposed to being one of the exam's fixed questions.</param>
/// <param name="Options">The options in the order this candidate saw them, each marking the correct one and whether the candidate chose it; null if the bank no longer has the question.</param>
/// <param name="Verdict">Whether the answer was correct, partly correct, wrong or missing; null until the attempt is submitted.</param>
/// <param name="Marks">The marks the answer earned, which may be negative; null until the attempt is submitted.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct.</param>
/// <param name="IsTextAnswer">Whether the candidate typed the answer, rather than choosing options.</param>
/// <param name="AnswerText">What the candidate typed, for a text question.</param>
/// <param name="AcceptedAnswers">The answers accepted for a text question, which staff see beside the paper.</param>
/// <param name="AiCheck">The AI evaluator's part in a typed answer that matches no accepted answer word for word, once the attempt is over; null for any other question.</param>
public sealed record AttemptPaperQuestionDto(
    Guid Id, string? Text, bool Drawn, IReadOnlyList<ReviewOptionDto>? Options = null, AnswerVerdict? Verdict = null, decimal? Marks = null,
    bool AllowsMultiple = false, bool IsTextAnswer = false, string? AnswerText = null, IReadOnlyList<string>? AcceptedAnswers = null,
    AttemptPaperAiCheckDto? AiCheck = null);

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
/// <param name="AiCheckAvailable">Whether staff can have a typed answer checked by the AI evaluator now: it is set up, and the attempt is over and its result still counts.</param>
public sealed record AttemptPaperDto(
    Guid AttemptId, int Number, bool HasDrawnQuestions, IReadOnlyList<AttemptPaperSectionDto> Sections,
    AttemptStatus Status = AttemptStatus.InProgress, decimal? Score = null, decimal? MaxScore = null, bool IsInvalidated = false,
    bool AiCheckAvailable = false);
