using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>An option as an author writes it, before it becomes part of a <see cref="Question"/>.</summary>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
public sealed record NewQuestionOption(string? Text, bool IsCorrect);

/// <summary>
/// A multiple-choice question with exactly one correct option (FR-5). The first cut of the bank is
/// deliberately this one type: single answer, with formatted text. The rules that make a question gradable live
/// here, so no caller can store one that could never be marked.
/// </summary>
public sealed class Question : AggregateRoot
{
    /// <summary>
    /// The longest stored question text, after trimming. The text is HTML, which can carry embedded images, so this
    /// is a ceiling on the whole document and not on what a candidate reads (see <see cref="MaxVisibleTextLength"/>).
    /// </summary>
    public const int MaxHtmlLength = 1_500_000;

    /// <summary>
    /// The most characters of readable text a question may have, ignoring markup. Counting what the candidate reads
    /// rather than the raw HTML keeps formatting from eating the allowance.
    /// </summary>
    public const int MaxVisibleTextLength = 4000;

    /// <summary>The most images one question may embed.</summary>
    public const int MaxImages = 5;

    /// <summary>
    /// The largest one embedded image, in bytes once decoded. Images live inside the question itself, so every
    /// response that carries the question carries them too; the limit keeps those responses small.
    /// </summary>
    public const int MaxImageBytes = 512 * 1024;

    /// <summary>The longest option text, after trimming.</summary>
    public const int MaxOptionTextLength = 1000;

    /// <summary>The fewest options a question may have.</summary>
    public const int MinOptions = 2;

    /// <summary>The most options a question may have.</summary>
    public const int MaxOptions = 6;

    private readonly List<QuestionOption> _options = [];

    /// <summary>
    /// The question text shown to the candidate, as sanitized HTML. Only a sanitizer-cleaned value may be stored here:
    /// it is rendered to every candidate, so anything else would be a script-injection route.
    /// </summary>
    public string Text { get; private set; }

    /// <summary>The answer options, in display order.</summary>
    public IReadOnlyList<QuestionOption> Options => _options.AsReadOnly();

    /// <summary>
    /// The chapter the question is filed under, or null when it is not filed anywhere. The chapter knows its book.
    /// Questions written before books existed stay unfiled.
    /// </summary>
    public Guid? ChapterId { get; private set; }

    /// <summary>The authoring user who created the question.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>When the question was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    // For EF Core.
    private Question() : base(Guid.Empty) => Text = null!;

    private Question(Guid id, string text, Guid? chapterId, Guid createdBy, DateTime createdAtUtc) : base(id)
    {
        Text = text;
        ChapterId = chapterId;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Creates a question after checking every rule that makes it gradable.</summary>
    /// <param name="text">The question text as sanitized HTML; leading and trailing whitespace is removed.</param>
    /// <param name="options">The answer options in display order; exactly one must be correct.</param>
    /// <param name="createdBy">The authoring user.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="chapterId">
    /// The chapter to file it under, or null for none. The caller has already checked that the chapter exists and is
    /// open; this aggregate cannot, because chapters belong to another aggregate.
    /// </param>
    /// <exception cref="InvalidQuestionError">
    /// The text is blank or larger than <see cref="MaxHtmlLength"/>, the number of options is outside <see cref="MinOptions"/> to
    /// <see cref="MaxOptions"/>, an option is blank or too long, or the options do not have exactly one correct answer.
    /// </exception>
    public static Question Create(
        string? text, IReadOnlyList<NewQuestionOption>? options, Guid createdBy, DateTime nowUtc, Guid? chapterId = null)
    {
        var trimmedText = text?.Trim();
        if (string.IsNullOrEmpty(trimmedText))
            throw new InvalidQuestionError("The question text is required.");
        if (trimmedText.Length > MaxHtmlLength)
            throw new InvalidQuestionError("The question is too large; use fewer or smaller images.");

        if (options is null || options.Count < MinOptions || options.Count > MaxOptions)
            throw new InvalidQuestionError($"A question needs between {MinOptions} and {MaxOptions} options.");

        if (options.Count(o => o is { IsCorrect: true }) != 1)
            throw new InvalidQuestionError("Exactly one option must be marked correct.");

        var question = new Question(Guid.NewGuid(), trimmedText, chapterId, createdBy, nowUtc);
        foreach (var option in options)
        {
            var optionText = option?.Text?.Trim();
            if (string.IsNullOrEmpty(optionText))
                throw new InvalidQuestionError("Every option needs text.");
            if (optionText.Length > MaxOptionTextLength)
                throw new InvalidQuestionError($"An option must be at most {MaxOptionTextLength} characters.");

            question._options.Add(new QuestionOption(question.Id, optionText, option!.IsCorrect, question._options.Count + 1));
        }

        return question;
    }
}
