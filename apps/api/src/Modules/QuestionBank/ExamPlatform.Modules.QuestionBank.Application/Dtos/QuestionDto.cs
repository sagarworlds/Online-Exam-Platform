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
/// <param name="Language">The language it is written in (FR-10): "en", "hi" or "mr".</param>
/// <param name="TranslationGroupId">Shared by this question and its translations, which are found by it (FR-10); a question with none is alone in its own group.</param>
/// <param name="ClassId">The class the book belongs to, or null when the book has none or the question is not filed.</param>
/// <param name="ClassName">That class's name, or null.</param>
/// <param name="IsTextAnswer">Whether the candidate types the answer instead of choosing an option (a text question).</param>
/// <param name="AcceptedAnswers">The answers a typed answer may be, for a text question; empty for a multiple-choice one.</param>
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
    string Status = "draft",
    string Language = "en",
    Guid? TranslationGroupId = null,
    Guid? ClassId = null,
    string? ClassName = null,
    bool IsTextAnswer = false,
    IReadOnlyList<string>? AcceptedAnswers = null);

/// <summary>One option of a <see cref="QuestionDto"/>.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this option is the right answer.</param>
/// <param name="IsPinned">Whether this option keeps its place when options are shuffled.</param>
public sealed record QuestionOptionDto(Guid Id, string Text, bool IsCorrect, bool IsPinned = false);
