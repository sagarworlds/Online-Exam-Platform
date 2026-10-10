using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Whether a candidate may review the answers of a submitted attempt, and if not, when they will be able to.</summary>
/// <param name="Available">Whether the review can be read now.</param>
/// <param name="Mode">How the exam's author decided it: Instant, Scheduled or Manual.</param>
/// <param name="AvailableFromUtc">From when it will be readable, when that is already decided (Scheduled); null otherwise.</param>
public sealed record AttemptReviewAvailabilityDto(bool Available, ExamResultReleaseMode Mode, DateTime? AvailableFromUtc);

/// <summary>An option as shown in a review: what it says, whether it is the right one, and whether the candidate chose it.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the correct option.</param>
/// <param name="WasChosen">Whether the candidate chose it.</param>
public sealed record ReviewOptionDto(Guid Id, string Text, bool IsCorrect, bool WasChosen);

/// <summary>A question as shown in a review, with how it was marked.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Text">The question text, as sanitized HTML; render it with an HTML sanitizer in place, never as trusted markup.</param>
/// <param name="Options">The options, in display order.</param>
/// <param name="Verdict">Whether the answer was correct, partly correct, wrong, or missing.</param>
/// <param name="Marks">The marks this question earned, which may be negative.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct, so the candidate had to choose exactly the correct ones.</param>
/// <param name="IsTextAnswer">Whether the candidate typed the answer. Such a question has no options.</param>
/// <param name="AnswerText">What the candidate typed, for a text question; null when they chose options or answered nothing.</param>
/// <param name="AcceptedAnswers">The answers that were accepted, for a text question, so the candidate can see what counted as right.</param>
/// <param name="Explanation">
/// Why the correct answer is correct, as plain text, or null when the author wrote none. Shown only here: a review is built only once the
/// exam's author has released the results (FR-33), and the explanation is the version the attempt sat, or the translation the candidate read.
/// </param>
public sealed record ReviewQuestionDto(
    Guid Id, string Text, IReadOnlyList<ReviewOptionDto> Options, AnswerVerdict Verdict, decimal Marks, bool AllowsMultiple = false,
    bool IsTextAnswer = false, string? AnswerText = null, IReadOnlyList<string>? AcceptedAnswers = null, string? Explanation = null);

/// <summary>One change to an attempt's score after it was first submitted (FR-31), most often an answer-key correction.</summary>
/// <param name="PreviousScore">The score before this revision.</param>
/// <param name="PreviousMaxScore">The marks available before this revision.</param>
/// <param name="NewScore">The score after this revision.</param>
/// <param name="NewMaxScore">The marks available after this revision.</param>
/// <param name="Reason">Why the score changed.</param>
/// <param name="RevisedAtUtc">When it changed.</param>
/// <param name="Version">The version of the result this revision produced: 2 for the first revision, 3 for the next. The result as first submitted is version 1.</param>
public sealed record ScoreRevisionDto(
    decimal PreviousScore, decimal PreviousMaxScore, decimal NewScore, decimal NewMaxScore, string Reason, DateTime RevisedAtUtc, int Version = 0);

/// <summary>A section of the exam in a review.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="Questions">The questions, in order.</param>
public sealed record ReviewSectionDto(Guid Id, string Name, IReadOnlyList<ReviewQuestionDto> Questions);

/// <summary>
/// A submitted attempt with its answer key (FR-32, FR-33). The only response that carries which option is correct, and only
/// ever built once the attempt is over and the exam's author has released the answers.
/// </summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="Number">Which attempt this is for the candidate at the exam, from 1.</param>
/// <param name="SubmittedAtUtc">When the attempt ended.</param>
/// <param name="AutoSubmitted">Whether it ended because time ran out.</param>
/// <param name="Score">The marks scored; the sum of the marks of the questions below.</param>
/// <param name="MaxScore">The marks available.</param>
/// <param name="CorrectCount">How many questions were answered correctly.</param>
/// <param name="WrongCount">How many were answered wrongly.</param>
/// <param name="UnansweredCount">How many were left unanswered.</param>
/// <param name="Sections">The questions, section by section.</param>
/// <param name="PartialCount">How many multiple-answer questions were answered partly right and earned part of the marks.</param>
/// <param name="Revisions">
/// How the score has changed since this attempt was first submitted, oldest first; empty for a result that has never
/// been revised. See <see cref="ScoreRevisionDto"/>.
/// </param>
/// <param name="ResultVersion">Which version of the result the score and marks above are: 1 as first submitted, one more for each revision (FR-31).</param>
/// <param name="DisputeWindow">Whether the answer key of this result can still be disputed, and until when; null in a review built without it.</param>
/// <param name="Disputes">The candidate's own disputes of this attempt's answer keys, oldest first, with how staff answered each; null in a review built without them.</param>
public sealed record AttemptReviewDto(
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
    int UnansweredCount,
    IReadOnlyList<ReviewSectionDto> Sections,
    int PartialCount = 0,
    IReadOnlyList<ScoreRevisionDto>? Revisions = null,
    int ResultVersion = 1,
    DisputeWindowDto? DisputeWindow = null,
    IReadOnlyList<MyDisputeDto>? Disputes = null);
