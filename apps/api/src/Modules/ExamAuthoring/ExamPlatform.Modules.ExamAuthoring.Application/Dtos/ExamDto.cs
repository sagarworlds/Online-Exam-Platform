using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// DTO for exam details.
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
    DateTime UpdatedAt
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
