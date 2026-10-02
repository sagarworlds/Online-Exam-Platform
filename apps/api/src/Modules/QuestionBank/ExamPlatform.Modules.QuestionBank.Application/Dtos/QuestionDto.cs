namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>A question as the authoring side sees it, answer key included.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Text">The question text.</param>
/// <param name="Options">The answer options in display order.</param>
/// <param name="CreatedBy">The authoring user.</param>
/// <param name="CreatedAtUtc">When the question was created.</param>
public sealed record QuestionDto(
    Guid Id,
    string Text,
    IReadOnlyList<QuestionOptionDto> Options,
    Guid CreatedBy,
    DateTime CreatedAtUtc);

/// <summary>One option of a <see cref="QuestionDto"/>.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this option is the right answer.</param>
public sealed record QuestionOptionDto(Guid Id, string Text, bool IsCorrect);
