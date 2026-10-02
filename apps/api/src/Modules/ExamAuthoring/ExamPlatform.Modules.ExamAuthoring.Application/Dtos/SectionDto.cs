namespace ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

/// <summary>One section of an exam with the questions in it.</summary>
/// <param name="Id">The section's id.</param>
/// <param name="Name">The section's name.</param>
/// <param name="TimeSeconds">The section's own time limit, if it has one.</param>
/// <param name="Order">Position in the exam, from 1.</param>
/// <param name="Questions">The section's questions in order.</param>
public record ExamSectionDto(
    Guid Id,
    string Name,
    int? TimeSeconds,
    int Order,
    IReadOnlyList<ExamQuestionDto> Questions
);

/// <summary>A question as it sits in an exam.</summary>
/// <param name="Id">The exam-question's id.</param>
/// <param name="QuestionId">The question's id in the question bank.</param>
/// <param name="Order">Position within the section, from 1.</param>
/// <param name="Text">The question text as sanitized HTML, read from the bank; null if the bank no longer has it.</param>
public record ExamQuestionDto(
    Guid Id,
    Guid QuestionId,
    int Order,
    string? Text
);
