using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Sets when an exam runs (FR-13).</summary>
/// <param name="ExamId">The exam to schedule.</param>
/// <param name="StartUtc">When the window opens, or null if the caller did not send it.</param>
/// <param name="EndUtc">When the window closes, or null if the caller did not send it.</param>
/// <param name="TimeZone">The IANA time zone the exam is described in; blank keeps the current one.</param>
/// <param name="LateEntryDeadlineUtc">The last moment a candidate may still start, if limited.</param>
/// <param name="DurationSeconds">How long one attempt lasts, if limited.</param>
public sealed record ScheduleExamCommand(
    Guid ExamId,
    DateTime? StartUtc,
    DateTime? EndUtc,
    string? TimeZone,
    DateTime? LateEntryDeadlineUtc,
    int? DurationSeconds);

/// <summary>Appends a section to an exam.</summary>
/// <param name="ExamId">The exam to add it to.</param>
/// <param name="Name">The section's name.</param>
/// <param name="TimeSeconds">An optional time limit for the section.</param>
public sealed record AddSectionCommand(Guid ExamId, string? Name, int? TimeSeconds);

/// <summary>Appends a question from the bank to a section.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="SectionId">The section to add the question to.</param>
/// <param name="QuestionId">The question's id in the question bank.</param>
public sealed record AddExamQuestionCommand(Guid ExamId, Guid SectionId, Guid QuestionId);

/// <summary>Changes the name and description candidates see.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="Name">The new name, or null if the caller did not send it.</param>
/// <param name="Description">The new description; blank means none.</param>
public sealed record UpdateExamDetailsCommand(Guid ExamId, string? Name, string? Description);

/// <summary>Renames a section of a draft exam and sets its time limit.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="SectionId">The section to change.</param>
/// <param name="Name">The new name, or null if the caller did not send it.</param>
/// <param name="TimeSeconds">The new time limit, or null for none.</param>
public sealed record EditSectionCommand(Guid ExamId, Guid SectionId, string? Name, int? TimeSeconds);

/// <summary>Takes a section, with its questions, out of a draft exam.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="SectionId">The section to remove.</param>
public sealed record RemoveSectionCommand(Guid ExamId, Guid SectionId);

/// <summary>Takes a question out of a section of a draft exam.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="SectionId">The section the question is in.</param>
/// <param name="QuestionId">The question's id in the question bank, the same id it was added with.</param>
public sealed record RemoveExamQuestionCommand(Guid ExamId, Guid SectionId, Guid QuestionId);

/// <summary>Chooses when candidates may see which of their answers were right.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="Mode">Instant, Scheduled or Manual, or null if the caller did not send it.</param>
/// <param name="ReleaseTimeUtc">From when the answers are visible; required for Scheduled.</param>
public sealed record SetResultReleaseCommand(Guid ExamId, ResultReleaseMode? Mode, DateTime? ReleaseTimeUtc);

/// <summary>Sets the marks a correct, an incorrect and an unattempted question earn.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="CorrectMarks">Marks for a correct answer, or null if the caller did not send it.</param>
/// <param name="IncorrectMarks">Marks for an incorrect answer (zero or negative), or null if not sent.</param>
/// <param name="UnattemptedMarks">Marks for an unanswered question (zero or negative), or null if not sent.</param>
public sealed record SetMarkingSchemeCommand(Guid ExamId, decimal? CorrectMarks, decimal? IncorrectMarks, decimal? UnattemptedMarks);

/// <summary>Sets how many attempts every enrolled candidate has at an exam.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="MaxAttempts">The attempts each candidate has, or null if the caller did not send it.</param>
public sealed record SetMaxAttemptsCommand(Guid ExamId, int? MaxAttempts);
