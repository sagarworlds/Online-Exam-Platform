namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// DTO for exam section details.
public record SectionDto(
    Guid Id,
    string Name,
    int? TimeSeconds,
    int Order,
    int QuestionCount,
    DateTime CreatedAt
);

/// Request to add a section to an exam.
public record AddSectionRequest(
    string Name,
    int? TimeSeconds = null
);

/// Request to add a question to a section.
public record AddQuestionRequest(
    Guid QuestionVersionId,
    int Order
);

/// DTO for a question within an exam (with answer metadata).
public record ExamQuestionDto(
    Guid Id,
    Guid QuestionVersionId,
    int Order
);
