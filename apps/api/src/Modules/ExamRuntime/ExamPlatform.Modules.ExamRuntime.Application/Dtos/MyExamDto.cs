using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Whether a candidate can start an exam right now.</summary>
public enum MyExamState
{
    /// <summary>The window has not opened yet.</summary>
    NotOpen,

    /// <summary>The window is open and a new attempt may be started.</summary>
    Open,

    /// <summary>The window, or the late-entry cutoff within it, has passed; no new attempt may be started.</summary>
    Closed,
}

/// <summary>One of a candidate's attempts at an exam, as listed: where it stands and what it scored.</summary>
/// <param name="Id">The attempt's id.</param>
/// <param name="Number">Which attempt this is for them at the exam, from 1.</param>
/// <param name="Status">Whether it is still open or already submitted.</param>
/// <param name="StartedAtUtc">When they started it.</param>
/// <param name="SubmittedAtUtc">When it ended, once submitted.</param>
/// <param name="AutoSubmitted">Whether it ended because time ran out.</param>
/// <param name="Score">The marks scored, once submitted.</param>
/// <param name="MaxScore">The marks available, once submitted.</param>
public sealed record AttemptSummaryDto(
    Guid Id,
    int Number,
    AttemptStatus Status,
    DateTime StartedAtUtc,
    DateTime? SubmittedAtUtc,
    bool AutoSubmitted,
    decimal? Score,
    decimal? MaxScore);

/// <summary>The rules of an exam that a candidate is told before starting it (FR-17); what the instructions page shows.</summary>
/// <param name="CorrectMarks">Marks for a correct answer.</param>
/// <param name="IncorrectMarks">Marks for a wrong answer; negative when wrong answers cost marks.</param>
/// <param name="UnattemptedMarks">Marks for a question left unanswered.</param>
/// <param name="PartialCredit">Whether a multiple-answer question can earn part of its marks.</param>
/// <param name="SectionLock">Whether leaving a section is final, so a candidate cannot go back to it.</param>
/// <param name="SectionCount">How many sections the exam has.</param>
public sealed record ExamRulesDto(
    decimal CorrectMarks,
    decimal IncorrectMarks,
    decimal UnattemptedMarks,
    bool PartialCredit,
    bool SectionLock,
    int SectionCount);

/// <summary>An exam a candidate is enrolled in, as shown on their exams page.</summary>
/// <param name="ExamId">The exam's id.</param>
/// <param name="Name">The exam's name.</param>
/// <param name="Description">An optional description.</param>
/// <param name="StartUtc">When the window opens.</param>
/// <param name="EndUtc">When the window closes.</param>
/// <param name="LateEntryDeadlineUtc">The last moment a new attempt may start, if limited.</param>
/// <param name="DurationSeconds">How long one attempt lasts, or null for "until the window closes".</param>
/// <param name="QuestionCount">How many questions the exam has.</param>
/// <param name="State">Whether an attempt can be started now.</param>
/// <param name="AttemptId">The candidate's latest attempt at this exam, or null if they have not started it.</param>
/// <param name="AttemptStatus">Whether that latest attempt is still open or already submitted; null without an attempt.</param>
/// <param name="Score">The marks the latest attempt scored, once it is submitted.</param>
/// <param name="MaxScore">The marks available, once the latest attempt is submitted.</param>
/// <param name="AttemptsAllowed">How many attempts they may make in all: the exam's limit per candidate, plus each extra attempt an administrator granted.</param>
/// <param name="AttemptsUsed">How many they have started.</param>
/// <param name="CanStartAttempt">Whether they may start a new attempt now: the window is open, none is in progress, and one is left.</param>
/// <param name="Attempts">Every attempt they have made, oldest first.</param>
/// <param name="CanRequestAttempt">Whether they may ask for another attempt now: the window is open, they have used every attempt they hold, none is in progress and no earlier request is waiting.</param>
/// <param name="AttemptRequest">The latest request they made for another attempt at this exam, if any.</param>
/// <param name="Rules">The exam's marking and navigation rules, for the instructions page.</param>
public sealed record MyExamDto(
    Guid ExamId,
    string Name,
    string? Description,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime? LateEntryDeadlineUtc,
    int? DurationSeconds,
    int QuestionCount,
    MyExamState State,
    Guid? AttemptId,
    AttemptStatus? AttemptStatus,
    decimal? Score,
    decimal? MaxScore,
    int AttemptsAllowed,
    int AttemptsUsed,
    bool CanStartAttempt,
    IReadOnlyList<AttemptSummaryDto> Attempts,
    bool CanRequestAttempt = false,
    MyAttemptRequestDto? AttemptRequest = null,
    ExamRulesDto? Rules = null);
