namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// Request to create a new exam.
public record CreateExamRequest(
    Guid SeriesId,
    string Name,
    string? Description = null
);
