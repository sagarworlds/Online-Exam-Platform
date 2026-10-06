namespace ExamPlatform.Modules.QuestionBank.Application.Dtos;

/// <summary>A question as the authoring side sees it, answer key included.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Text">The question text, as sanitized HTML.</param>
/// <param name="Options">The answer options in display order.</param>
/// <param name="CreatedBy">The authoring user.</param>
/// <param name="CreatedAtUtc">When the question was created.</param>
/// <param name="ChapterId">The chapter the question is filed under, or null when it is not filed.</param>
/// <param name="ChapterTitle">That chapter's title, or null.</param>
/// <param name="BookId">The book the chapter belongs to, or null.</param>
/// <param name="BookName">That book's name, or null.</param>
/// <param name="Usage">Whether exams contain the question and candidates have answered it.</param>
/// <param name="Difficulty">"easy", "medium" or "hard", or null when the author has not said.</param>
/// <param name="Topics">The question's topics, lower case.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct.</param>
/// <param name="Status">Where the question is in the review workflow (FR-8): "draft", "in_review", "approved" or "retired".</param>
public sealed record QuestionDto(
    Guid Id,
    string Text,
    IReadOnlyList<QuestionOptionDto> Options,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    Guid? ChapterId = null,
    string? ChapterTitle = null,
    Guid? BookId = null,
    string? BookName = null,
    QuestionUsageDto? Usage = null,
    string? Difficulty = null,
    IReadOnlyList<string>? Topics = null,
    bool AllowsMultiple = false,
    string Status = "draft");

/// <summary>One option of a <see cref="QuestionDto"/>.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this option is the right answer.</param>
/// <param name="IsPinned">Whether this option keeps its place when options are shuffled.</param>
public sealed record QuestionOptionDto(Guid Id, string Text, bool IsCorrect, bool IsPinned = false);
