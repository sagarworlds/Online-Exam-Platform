namespace ExamPlatform.Modules.QuestionBank.Contracts;

/// <summary>A question as another module sees it.</summary>
/// <param name="Id">The question's id.</param>
/// <param name="Text">The question text, as sanitized HTML.</param>
/// <param name="Options">The answer options, in display order.</param>
/// <param name="ChapterId">The chapter the question is filed under, or null when it is not filed.</param>
/// <param name="BookId">The book that chapter belongs to, or null when the question is not filed.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct, so a candidate chooses a set and must choose exactly the correct ones.</param>
/// <param name="UnusableReason">Why the question cannot be added to an exam (it is retired, or exams need approved questions and it is not), or null when it can (FR-8).</param>
/// <param name="VersionNumber">The version of the question this is the content of (FR-7): the current one from <see cref="IQuestionBank.GetAsync"/>, or the one asked for from <see cref="IQuestionBank.GetVersionsAsync"/>.</param>
/// <param name="IsTextAnswer">Whether the candidate types the answer instead of choosing an option. Such a question has no options.</param>
/// <param name="AcceptedAnswers">For a text question, the answers a typed answer may be. Never to be sent to a candidate.</param>
public sealed record QuestionSnapshot(
    Guid Id,
    string Text,
    IReadOnlyList<QuestionOptionSnapshot> Options,
    Guid? ChapterId = null,
    Guid? BookId = null,
    bool AllowsMultiple = false,
    int VersionNumber = 1,
    string? UnusableReason = null,
    bool IsTextAnswer = false,
    IReadOnlyList<string>? AcceptedAnswers = null);

/// <summary>One question, and which version of it to read.</summary>
/// <param name="QuestionId">The question's id.</param>
/// <param name="VersionNumber">The version wanted, or null for the current one (what an attempt made before versions were recorded reads).</param>
public sealed record QuestionVersionRef(Guid QuestionId, int? VersionNumber);

/// <summary>One answer option of a <see cref="QuestionSnapshot"/>.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer. Never to be sent to a candidate.</param>
/// <param name="IsPinned">Whether the option keeps its place when options are shuffled for a candidate.</param>
public sealed record QuestionOptionSnapshot(Guid Id, string Text, bool IsCorrect, bool IsPinned = false);
