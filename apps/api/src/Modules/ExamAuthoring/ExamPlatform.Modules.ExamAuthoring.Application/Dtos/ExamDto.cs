using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// <summary>An exam as the authoring side sees it.</summary>
/// <remarks>
/// <c>Sections</c> is filled in when one exam is read and is null in a listing. <c>Scope</c> carries the book and
/// chapter names when the exam is reported through <c>ExamDtoFactory</c>; the plain mapping has the ids only.
/// </remarks>
public record ExamDto(
    Guid Id,
    Guid? SeriesId,
    string Name,
    string? Description,
    ExamStatus Status,
    ExamConfigDto Config,
    DateTime ScheduledStartTime,
    DateTime ScheduledEndTime,
    DateTime? LateEntryDeadline,
    string TimeZone,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsScheduled = false,
    IReadOnlyList<ExamSectionDto>? Sections = null,
    ExamScopeDto? Scope = null,
    ProctoringDto? Proctoring = null,
    string? Instructions = null
);

/// <summary>An exam's proctoring as an author reads it (FR-46): which profile its settings amount to, and the notice candidates are shown.</summary>
/// <param name="Profile">The id of the profile the exam's settings match, or <c>CUSTOM</c> when they were set one by one.</param>
/// <param name="ProfileName">What to call it.</param>
/// <param name="Notice">The notice candidates are shown before they start, written from the exam's settings.</param>
public record ProctoringDto(string Profile, string ProfileName, IReadOnlyList<string> Notice);

/// <summary>A proctoring profile as an author chooses it (FR-46).</summary>
/// <param name="Id">The profile's id, as the requirements name it.</param>
/// <param name="Name">What an author reads.</param>
/// <param name="Description">Who it is for.</param>
/// <param name="Available">Whether it can be chosen; a profile that promises something not built yet is listed but not offered.</param>
/// <param name="UnavailableReason">Why it cannot be chosen, when it cannot.</param>
/// <param name="ContentProtection">Whether it turns copy, paste, right-click and print off.</param>
/// <param name="FocusViolationLimit">How many departures from the exam page it allows; 0 means not watched.</param>
/// <param name="Notice">What candidates would be told under it.</param>
public record ProctoringProfileDto(
    string Id,
    string Name,
    string Description,
    bool Available,
    string? UnavailableReason,
    bool ContentProtection,
    int FocusViolationLimit,
    IReadOnlyList<string> Notice);

/// DTO for exam configuration.
public record ExamConfigDto(
    int? TotalTimeSeconds,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    bool SectionLockEnabled,
    bool CalculatorAllowed,
    bool ScratchpadAllowed,
    int MaxAttempts,
    int MaxRetakes,
    ResultReleaseMode ResultReleaseMode,
    DateTime? ResultReleaseTime,
    MarkingScheme MarkingScheme,
    bool ContentProtection = true,
    int FocusViolationLimit = 0
);
