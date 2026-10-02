using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>An option as an author writes it, before it becomes part of a <see cref="Question"/>.</summary>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
public sealed record NewQuestionOption(string? Text, bool IsCorrect);

/// <summary>An option as an author left it after editing, which may be one the question already has or a new one.</summary>
/// <param name="Id">The id of the existing option this edits, or null for an option that is new.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
public sealed record QuestionOptionEdit(Guid? Id, string? Text, bool IsCorrect);

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
        var trimmedText = RequireText(text);
        RequireOptionShape(options?.Count, options?.Count(o => o is { IsCorrect: true }) ?? 0);

        var question = new Question(Guid.NewGuid(), trimmedText, chapterId, createdBy, nowUtc);
        foreach (var option in options!)
        {
            var optionText = RequireOptionText(option?.Text);
            question._options.Add(new QuestionOption(question.Id, optionText, option!.IsCorrect, question._options.Count + 1));
        }

        return question;
    }

    /// <summary>
    /// Replaces the question's text and options with an author's edit, under the same rules as <see cref="Create"/>.
    /// Options are matched by id: an option that keeps its id keeps its identity (a saved answer points at it), one without an
    /// id is new, and one left out is removed. The position in <paramref name="options"/> becomes the display order.
    /// </summary>
    /// <remarks>
    /// Nothing changes unless the whole edit is acceptable, so a refused edit leaves the question exactly as it was.
    /// </remarks>
    /// <param name="text">The new question text as sanitized HTML.</param>
    /// <param name="options">All the options after the edit, in display order.</param>
    /// <param name="answered">
    /// Whether any candidate has answered the question. The aggregate cannot know this (attempts belong to another module),
    /// so the caller says; when true, only the wording of the text and of each option may change.
    /// </param>
    /// <exception cref="InvalidQuestionError">The edit breaks a rule of <see cref="Create"/>, or names an option this question does not have.</exception>
    /// <exception cref="QuestionLockedError">
    /// The question has been answered and the edit changes which option is correct, or adds, removes or reorders options.
    /// </exception>
    public void Revise(string? text, IReadOnlyList<QuestionOptionEdit>? options, bool answered)
    {
        var trimmedText = RequireText(text);
        RequireOptionShape(options?.Count, options?.Count(o => o is { IsCorrect: true }) ?? 0);

        var edits = options!;
        var optionTexts = edits.Select(o => RequireOptionText(o?.Text)).ToList();

        var existing = _options.ToDictionary(o => o.Id);
        var namedIds = edits.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToList();
        if (namedIds.Any(id => !existing.ContainsKey(id)))
            throw new InvalidQuestionError("An option does not belong to this question.");
        if (namedIds.Distinct().Count() != namedIds.Count)
            throw new InvalidQuestionError("An option appears more than once.");

        if (answered)
            EnsureOnlyWordingChanges(edits);

        Text = trimmedText;

        var revised = new List<QuestionOption>(edits.Count);
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (edit.Id is { } id)
            {
                var option = existing[id];
                option.Revise(optionTexts[i], edit.IsCorrect, i + 1);
                revised.Add(option);
            }
            else
            {
                revised.Add(new QuestionOption(Id, optionTexts[i], edit.IsCorrect, i + 1));
            }
        }

        _options.Clear();
        _options.AddRange(revised);
    }

    /// <summary>Files the question under a chapter, wherever it was before. Nothing about its content changes.</summary>
    /// <param name="chapterId">
    /// The chapter to file it under. The caller has already checked that the chapter exists and is open; this aggregate
    /// cannot, because chapters belong to another aggregate.
    /// </param>
    /// <returns><see langword="true"/> when the question moved; <see langword="false"/> when it was already filed there.</returns>
    public bool FileUnder(Guid chapterId)
    {
        if (ChapterId == chapterId)
            return false;

        ChapterId = chapterId;
        return true;
    }

    // Once candidates have answered, the key and the list of options are part of their results. Wording is the one thing
    // that can still be corrected without touching any of that: the same options, in the same order, with the same one correct.
    private void EnsureOnlyWordingChanges(IReadOnlyList<QuestionOptionEdit> edits)
    {
        var current = _options.OrderBy(o => o.Order).ToList();

        var unchanged = edits.Count == current.Count
            && current.Select((option, i) => edits[i].Id == option.Id && edits[i].IsCorrect == option.IsCorrect).All(same => same);

        if (!unchanged)
            throw new QuestionLockedError();
    }

    private static string RequireText(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidQuestionError("The question text is required.");
        if (trimmed.Length > MaxHtmlLength)
            throw new InvalidQuestionError("The question is too large; use fewer or smaller images.");

        return trimmed;
    }

    private static void RequireOptionShape(int? optionCount, int correctCount)
    {
        if (optionCount is null || optionCount < MinOptions || optionCount > MaxOptions)
            throw new InvalidQuestionError($"A question needs between {MinOptions} and {MaxOptions} options.");
        if (correctCount != 1)
            throw new InvalidQuestionError("Exactly one option must be marked correct.");
    }

    private static string RequireOptionText(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidQuestionError("Every option needs text.");
        if (trimmed.Length > MaxOptionTextLength)
            throw new InvalidQuestionError($"An option must be at most {MaxOptionTextLength} characters.");

        return trimmed;
    }
}
