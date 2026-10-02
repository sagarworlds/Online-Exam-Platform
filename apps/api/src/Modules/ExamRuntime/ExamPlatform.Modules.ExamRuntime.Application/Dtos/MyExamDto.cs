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
/// <param name="AttemptId">The candidate's attempt at this exam, or null if they have not started it.</param>
/// <param name="AttemptStatus">Whether that attempt is still open or already submitted; null without an attempt.</param>
/// <param name="Score">The marks scored, once the attempt is submitted.</param>
/// <param name="MaxScore">The marks available, once the attempt is submitted.</param>
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
    decimal? MaxScore);
