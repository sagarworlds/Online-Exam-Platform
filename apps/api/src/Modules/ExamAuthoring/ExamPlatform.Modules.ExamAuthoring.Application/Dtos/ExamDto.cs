using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// <summary>An exam as the authoring side sees it.</summary>
/// <param name="Sections">The sections with their questions; filled in when one exam is read, null in a listing.</param>
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
    IReadOnlyList<ExamSectionDto>? Sections = null
);

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
    MarkingScheme MarkingScheme
);
