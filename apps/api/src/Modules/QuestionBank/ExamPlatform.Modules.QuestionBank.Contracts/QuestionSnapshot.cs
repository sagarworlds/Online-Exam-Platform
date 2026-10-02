namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>A question as another module sees it.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Text">The question text, as sanitized HTML.</param>
/// <param name="Options">The answer options, in display order.</param>
/// <param name="ChapterId">The chapter the question is filed under, or null when it is not filed.</param>
/// <param name="BookId">The book that chapter belongs to, or null when the question is not filed.</param>
public sealed record QuestionSnapshot(
    Guid Id,
    string Text,
    IReadOnlyList<QuestionOptionSnapshot> Options,
    Guid? ChapterId = null,
    Guid? BookId = null);

/// <summary>One answer option of a <see cref="QuestionSnapshot"/>.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer. Never to be sent to a candidate.</param>
public sealed record QuestionOptionSnapshot(Guid Id, string Text, bool IsCorrect);
