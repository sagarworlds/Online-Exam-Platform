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
public sealed record ReviewQuestionDto(
    Guid Id, string Text, IReadOnlyList<ReviewOptionDto> Options, AnswerVerdict Verdict, decimal Marks, bool AllowsMultiple = false);

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
    int PartialCount = 0);
