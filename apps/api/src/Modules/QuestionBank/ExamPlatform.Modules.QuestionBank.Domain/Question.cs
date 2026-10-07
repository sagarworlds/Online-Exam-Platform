using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>An option as an author writes it, before it becomes part of a <see cref="Question"/>.</summary>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
/// <param name="IsPinned">Whether the option keeps its place when options are shuffled.</param>
public sealed record NewQuestionOption(string? Text, bool IsCorrect, bool IsPinned = false);

/// <summary>An option as an author left it after editing, which may be one the question already has or a new one.</summary>
/// <param name="Id">The id of the existing option this edits, or null for an option that is new.</param>
/// <param name="Text">The option text.</param>
/// <param name="IsCorrect">Whether this is the right answer.</param>
/// <param name="IsPinned">Whether the option keeps its place when options are shuffled.</param>
public sealed record QuestionOptionEdit(Guid? Id, string? Text, bool IsCorrect, bool IsPinned = false);

/// <summary>
/// A multiple-choice question (FR-5): by default exactly one correct option, or, when <see cref="AllowsMultiple"/> is set,
/// one or more, all of which a candidate must choose to be marked right. The rules that make a question gradable live
/// here, so no caller can store one that could never be marked. Every accepted change to that gradable content takes a
/// new <see cref="QuestionVersion"/> (FR-7); see <see cref="Versions"/>.
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
    private readonly List<QuestionVersion> _versions = [];

    /// <summary>
    /// The question text shown to the candidate, as sanitized HTML. Only a sanitizer-cleaned value may be stored here:
    /// it is rendered to every candidate, so anything else would be a script-injection route.
    /// </summary>
    public string Text { get; private set; }

    /// <summary>
    /// The readable text of <see cref="Text"/> with the markup taken out, kept so the bank can be searched by what a candidate reads
    /// rather than by tag names and image data. It follows the text: whoever sets or changes the text sets this too
    /// (see <see cref="IndexText"/>). Empty for a question stored before searching existed and not yet edited.
    /// </summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>
    /// What the question's stem is looked up by to find another question that says the same thing (FR-9); see
    /// <see cref="QuestionFingerprint"/>. Set with <see cref="SearchText"/>, empty when there is no readable text.
    /// </summary>
    public string TextKey { get; private set; } = string.Empty;

    /// <summary>The answer options, in display order.</summary>
    public IReadOnlyList<QuestionOption> Options => _options.AsReadOnly();

    /// <summary>
    /// Every version this question has had (FR-7), oldest first. Not necessarily loaded: a caller that only needs the
    /// question as it is now may load it without this history.
    /// </summary>
    public IReadOnlyList<QuestionVersion> Versions => _versions.AsReadOnly();

    /// <summary>The number of the version currently in force; 1 for a question that has never been revised.</summary>
    public int CurrentVersionNumber => _versions.Count == 0 ? 1 : _versions[^1].VersionNumber;

    /// <summary>
    /// The chapter the question is filed under, or null when it is not filed anywhere. The chapter knows its book.
    /// Questions written before books existed stay unfiled.
    /// </summary>
    public Guid? ChapterId { get; private set; }

    /// <summary>The most topics one question may carry.</summary>
    public const int MaxTopics = 5;

    /// <summary>The longest topic, after trimming.</summary>
    public const int MaxTopicLength = 40;

    /// <summary>
    /// Whether more than one option may be correct, so a candidate chooses a set of options and is marked right only when the set
    /// is exactly the correct ones. False for the single-answer question every question was before this existed.
    /// </summary>
    public bool AllowsMultiple { get; private set; }

    /// <summary>How hard the author judges the question to be, or null when they have not said.</summary>
    public QuestionDifficulty? Difficulty { get; private set; }

    /// <summary>
    /// Free-text topics, such as "fractions", in lower case and without duplicates. They are labels for finding questions in the
    /// bank and take no part in marking, so they can change at any time, even after candidates have answered.
    /// </summary>
    public string[] Topics { get; private set; } = [];

    /// <summary>The authoring user who created the question.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>When the question was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// When staff last corrected which options are right after candidates had already answered the question
    /// (<see cref="CorrectAnswerKey"/>), or null if that has never happened. Kept for display, not for marking:
    /// a correction is a deliberate exception to the usual "answered means locked" rule, so it is traceable.
    /// </summary>
    public DateTime? AnswerKeyCorrectedAtUtc { get; private set; }

    /// <summary>
    /// Where the question is in the review workflow (FR-8). A new question is a draft. Changing an approved or in-review question's
    /// content (<see cref="Revise"/>) returns it to a draft, so what a reviewer approved is always what is stored; an answer-key
    /// correction does not, because it is staff correcting a mistake in something already in use.
    /// </summary>
    public QuestionStatus Status { get; private set; } = QuestionStatus.Draft;

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
    /// <param name="difficulty">How hard the question is, or null for unsaid.</param>
    /// <param name="topics">The question's topics; see <see cref="Classify"/>.</param>
    /// <param name="allowsMultiple">Whether more than one option may be correct.</param>
    /// <exception cref="InvalidQuestionError">
    /// The text is blank or larger than <see cref="MaxHtmlLength"/>, the number of options is outside <see cref="MinOptions"/> to
    /// <see cref="MaxOptions"/>, an option is blank or too long, the options do not have exactly one correct answer, or the topics
    /// break the rules of <see cref="Classify"/>.
    /// </exception>
    public static Question Create(
        string? text, IReadOnlyList<NewQuestionOption>? options, Guid createdBy, DateTime nowUtc, Guid? chapterId = null,
        QuestionDifficulty? difficulty = null, IReadOnlyList<string?>? topics = null, bool allowsMultiple = false)
    {
        var trimmedText = RequireText(text);
        RequireOptionShape(options?.Count, options?.Count(o => o is { IsCorrect: true }) ?? 0, allowsMultiple);

        var question = new Question(Guid.NewGuid(), trimmedText, chapterId, createdBy, nowUtc) { AllowsMultiple = allowsMultiple };
        question.Classify(difficulty, topics);
        foreach (var option in options!)
        {
            var optionText = RequireOptionText(option?.Text);
            question._options.Add(new QuestionOption(question.Id, optionText, option!.IsCorrect, question._options.Count + 1, option.IsPinned));
        }

        question.Snapshot(nowUtc);
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
    /// <param name="allowsMultiple">Whether more than one option may be correct after the edit.</param>
    /// <param name="nowUtc">
    /// The current instant, recorded on the version this edit takes (FR-7). Defaults to <see cref="DateTime.UtcNow"/> so
    /// a caller that does not care about versioning's timestamp need not supply one.
    /// </param>
    /// <exception cref="InvalidQuestionError">The edit breaks a rule of <see cref="Create"/>, or names an option this question does not have.</exception>
    /// <exception cref="QuestionLockedError">
    /// The question has been answered and the edit changes which option is correct, or adds, removes or reorders options.
    /// </exception>
    public void Revise(string? text, IReadOnlyList<QuestionOptionEdit>? options, bool answered, bool allowsMultiple = false, DateTime? nowUtc = null)
    {
        var trimmedText = RequireText(text);
        RequireOptionShape(options?.Count, options?.Count(o => o is { IsCorrect: true }) ?? 0, allowsMultiple);

        var edits = options!;
        var optionTexts = edits.Select(o => RequireOptionText(o?.Text)).ToList();

        var existing = _options.ToDictionary(o => o.Id);
        var namedIds = edits.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToList();
        if (namedIds.Any(id => !existing.ContainsKey(id)))
            throw new InvalidQuestionError("An option does not belong to this question.");
        if (namedIds.Distinct().Count() != namedIds.Count)
            throw new InvalidQuestionError("An option appears more than once.");

        if (answered)
            EnsureOnlyWordingChanges(edits, allowsMultiple);

        var before = ContentFingerprint();
        Text = trimmedText;
        AllowsMultiple = allowsMultiple;

        var revised = new List<QuestionOption>(edits.Count);
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (edit.Id is { } id)
            {
                var option = existing[id];
                option.Revise(optionTexts[i], edit.IsCorrect, i + 1, edit.IsPinned);
                revised.Add(option);
            }
            else
            {
                revised.Add(new QuestionOption(Id, optionTexts[i], edit.IsCorrect, i + 1, edit.IsPinned));
            }
        }

        _options.Clear();
        _options.AddRange(revised);
        Snapshot(nowUtc ?? DateTime.UtcNow);

        // Saving without changing anything is not an edit, and must not undo an approval.
        if (Status is QuestionStatus.InReview or QuestionStatus.Approved && ContentFingerprint() != before)
            Status = QuestionStatus.Draft;
    }

    private string ContentFingerprint() =>
        Text + "|" + AllowsMultiple + "|" + string.Join(";", _options.OrderBy(o => o.Order).Select(o => $"{o.Id}:{o.Text}:{o.IsCorrect}:{o.IsPinned}:{o.Order}"));

    /// <summary>Puts a draft forward for review (FR-8).</summary>
    /// <param name="byUserId">The author or other staff member putting it forward.</param>
    /// <param name="byLabel">How to show who they are.</param>
    /// <param name="comment">An optional note to the reviewer.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The entry for the review thread; the caller stores it.</returns>
    /// <exception cref="InvalidQuestionStatusError">The question is not a draft.</exception>
    /// <exception cref="InvalidQuestionError">The comment is too long.</exception>
    public QuestionReviewEntry SubmitForReview(Guid byUserId, string? byLabel, string? comment, DateTime nowUtc)
    {
        Require(Status == QuestionStatus.Draft, "Only a draft can be put forward for review.");
        return Move(QuestionStatus.InReview, QuestionReviewEntryKind.Submitted, byUserId, byLabel, OptionalComment(comment), nowUtc);
    }

    /// <summary>Approves a question that is in review (FR-8).</summary>
    /// <param name="byUserId">The reviewer.</param>
    /// <param name="byLabel">How to show who they are.</param>
    /// <param name="comment">An optional note.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The entry for the review thread; the caller stores it.</returns>
    /// <exception cref="InvalidQuestionStatusError">The question is not in review.</exception>
    /// <exception cref="InvalidQuestionError">The comment is too long.</exception>
    public QuestionReviewEntry Approve(Guid byUserId, string? byLabel, string? comment, DateTime nowUtc)
    {
        Require(Status == QuestionStatus.InReview, "Only a question that is in review can be approved.");
        return Move(QuestionStatus.Approved, QuestionReviewEntryKind.Approved, byUserId, byLabel, OptionalComment(comment), nowUtc);
    }

    /// <summary>Sends a question in review back to its author as a draft, with the reason (FR-8).</summary>
    /// <param name="byUserId">The reviewer.</param>
    /// <param name="byLabel">How to show who they are.</param>
    /// <param name="comment">What has to change; required, because the author needs to know.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The entry for the review thread; the caller stores it.</returns>
    /// <exception cref="InvalidQuestionStatusError">The question is not in review.</exception>
    /// <exception cref="InvalidQuestionError">There is no comment, or it is too long.</exception>
    public QuestionReviewEntry RequestChanges(Guid byUserId, string? byLabel, string? comment, DateTime nowUtc)
    {
        Require(Status == QuestionStatus.InReview, "Only a question that is in review can be sent back.");
        return Move(QuestionStatus.Draft, QuestionReviewEntryKind.ChangesRequested, byUserId, byLabel, RequiredComment(comment, "Say what has to change."), nowUtc);
    }

    /// <summary>Takes a question out of use (FR-8).</summary>
    /// <param name="byUserId">Who is retiring it.</param>
    /// <param name="byLabel">How to show who they are.</param>
    /// <param name="comment">An optional reason.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The entry for the review thread; the caller stores it.</returns>
    /// <exception cref="InvalidQuestionStatusError">The question is already retired.</exception>
    /// <exception cref="InvalidQuestionError">The comment is too long.</exception>
    public QuestionReviewEntry Retire(Guid byUserId, string? byLabel, string? comment, DateTime nowUtc)
    {
        Require(Status != QuestionStatus.Retired, "The question is already retired.");
        return Move(QuestionStatus.Retired, QuestionReviewEntryKind.Retired, byUserId, byLabel, OptionalComment(comment), nowUtc);
    }

    /// <summary>Brings a retired question back as a draft, which has to be reviewed again (FR-8).</summary>
    /// <param name="byUserId">Who is restoring it.</param>
    /// <param name="byLabel">How to show who they are.</param>
    /// <param name="comment">An optional note.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The entry for the review thread; the caller stores it.</returns>
    /// <exception cref="InvalidQuestionStatusError">The question is not retired.</exception>
    /// <exception cref="InvalidQuestionError">The comment is too long.</exception>
    public QuestionReviewEntry Restore(Guid byUserId, string? byLabel, string? comment, DateTime nowUtc)
    {
        Require(Status == QuestionStatus.Retired, "Only a retired question can be restored.");
        return Move(QuestionStatus.Draft, QuestionReviewEntryKind.Restored, byUserId, byLabel, OptionalComment(comment), nowUtc);
    }

    /// <summary>Adds a comment to the review thread, in any status (FR-8).</summary>
    /// <param name="byUserId">Who is commenting.</param>
    /// <param name="byLabel">How to show who they are.</param>
    /// <param name="comment">The comment; required.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The entry for the review thread; the caller stores it.</returns>
    /// <exception cref="InvalidQuestionError">There is no comment, or it is too long.</exception>
    public QuestionReviewEntry Comment(Guid byUserId, string? byLabel, string? comment, DateTime nowUtc) =>
        new(Id, QuestionReviewEntryKind.Commented, byUserId, byLabel, RequiredComment(comment, "Write a comment."), CurrentVersionNumber, Status, nowUtc);

    private QuestionReviewEntry Move(
        QuestionStatus to, QuestionReviewEntryKind kind, Guid byUserId, string? byLabel, string comment, DateTime nowUtc)
    {
        Status = to;
        return new QuestionReviewEntry(Id, kind, byUserId, byLabel, comment, CurrentVersionNumber, to, nowUtc);
    }

    private static void Require(bool allowed, string message)
    {
        if (!allowed)
            throw new InvalidQuestionStatusError(message);
    }

    private static string OptionalComment(string? comment)
    {
        var text = comment?.Trim() ?? string.Empty;
        if (text.Length > QuestionReviewEntry.MaxCommentLength)
            throw new InvalidQuestionError($"A comment can have at most {QuestionReviewEntry.MaxCommentLength} characters.");
        return text;
    }

    private static string RequiredComment(string? comment, string whenMissing)
    {
        var text = OptionalComment(comment);
        return text.Length == 0 ? throw new InvalidQuestionError(whenMissing) : text;
    }

    /// <summary>
    /// Corrects which options are right, overriding the lock <see cref="Revise"/> enforces once candidates have answered
    /// (FR-31): an answer-key dispute means the key itself was wrong, which "only wording can change" cannot fix. Unlike
    /// <see cref="Revise"/>, this never touches text, options, their order, or which are pinned — only which are correct —
    /// so nothing about how a candidate's shuffled paper looked, or what they could have chosen, is rewritten after the
    /// fact; only the verdict on what they did choose.
    /// </summary>
    /// <param name="correctOptionIds">The options that are actually correct, replacing the current answer key.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns><see langword="true"/> when the key actually changed; <see langword="false"/> when it already matched (a no-op, not an error).</returns>
    /// <exception cref="InvalidQuestionError">
    /// An id does not belong to this question's options, or the count of correct options breaks <see cref="Create"/>'s shape rule
    /// (exactly one unless <see cref="AllowsMultiple"/>, otherwise at least one and not all of them).
    /// </exception>
    public bool CorrectAnswerKey(IReadOnlyCollection<Guid> correctOptionIds, DateTime nowUtc)
    {
        var distinct = (correctOptionIds ?? []).Distinct().ToHashSet();
        var known = _options.Select(o => o.Id).ToHashSet();
        if (!distinct.IsSubsetOf(known))
            throw new InvalidQuestionError("A correct option must belong to this question.");

        RequireOptionShape(_options.Count, distinct.Count, AllowsMultiple);

        var current = _options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        if (current.SetEquals(distinct))
            return false;

        foreach (var option in _options)
            option.SetCorrect(distinct.Contains(option.Id));

        AnswerKeyCorrectedAtUtc = nowUtc;
        Snapshot(nowUtc);
        return true;
    }

    /// <summary>Records the readable text of the question for searching.</summary>
    /// <param name="plainText">The question text with all markup removed; null counts as empty.</param>
    public void IndexText(string? plainText)
    {
        SearchText = plainText?.Trim() ?? string.Empty;
        TextKey = QuestionFingerprint.KeyOf(SearchText);
    }

    /// <summary>Sets how hard the question is and which topics it covers, replacing what was there.</summary>
    /// <remarks>
    /// Allowed whatever the question's use: labels never reach a candidate or a score. Topics are trimmed, lower-cased and
    /// de-duplicated so "Fractions" and "fractions " are one topic and a filter by topic finds both.
    /// </remarks>
    /// <param name="difficulty">The difficulty, or null to leave it unsaid.</param>
    /// <param name="topics">The topics, or null for none; blank ones are ignored.</param>
    /// <exception cref="InvalidQuestionError">There are more than <see cref="MaxTopics"/> topics or one is longer than <see cref="MaxTopicLength"/>.</exception>
    public void Classify(QuestionDifficulty? difficulty, IReadOnlyList<string?>? topics)
    {
        if (difficulty is { } level && !Enum.IsDefined(level))
            throw new InvalidQuestionError("The difficulty must be easy, medium or hard.");

        var normalized = (topics ?? []).Select(NormalizeTopic).Where(t => t.Length > 0).Distinct().ToArray();
        if (normalized.Length > MaxTopics)
            throw new InvalidQuestionError($"A question can have at most {MaxTopics} topics.");
        if (normalized.Any(t => t.Length > MaxTopicLength))
            throw new InvalidQuestionError($"A topic must be at most {MaxTopicLength} characters.");

        Difficulty = difficulty;
        Topics = normalized;
    }

    /// <summary>Puts a topic in the form it is stored and searched in: trimmed, inner whitespace collapsed to one space, lower case.</summary>
    /// <param name="topic">The topic as typed; null counts as blank.</param>
    /// <returns>The normalized topic, empty when the input was blank.</returns>
    public static string NormalizeTopic(string? topic) =>
        string.Join(' ', (topic ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

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

    // Classifying (labels) and filing (where the question sits) never reach a candidate or affect marking, so they do not
    // take a new version; only a change that could change what a candidate saw or how they were marked does.
    private void Snapshot(DateTime nowUtc)
    {
        var options = _options.OrderBy(o => o.Order)
            .Select(o => new QuestionVersionOption(o.Id, o.Text, o.IsCorrect, o.Order, o.IsPinned))
            .ToList();
        _versions.Add(new QuestionVersion(Id, _versions.Count + 1, Text, AllowsMultiple, options, nowUtc));
    }

    // Once candidates have answered, the key and the list of options are part of their results. Wording is the one thing
    // that can still be corrected without touching any of that: the same options, in the same order, with the same one correct and
    // the same ones pinned (a pin decides where an option lands in each candidate's shuffled order, which a review must reproduce).
    private void EnsureOnlyWordingChanges(IReadOnlyList<QuestionOptionEdit> edits, bool allowsMultiple)
    {
        var current = _options.OrderBy(o => o.Order).ToList();

        // Whether the question takes one answer or several is part of how stored answers were marked, like which option is correct.
        var unchanged = allowsMultiple == AllowsMultiple && edits.Count == current.Count
            && current.Select((option, i) => edits[i].Id == option.Id && edits[i].IsCorrect == option.IsCorrect && edits[i].IsPinned == option.IsPinned).All(same => same);

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

    private static void RequireOptionShape(int? optionCount, int correctCount, bool allowsMultiple)
    {
        if (optionCount is null || optionCount < MinOptions || optionCount > MaxOptions)
            throw new InvalidQuestionError($"A question needs between {MinOptions} and {MaxOptions} options.");

        if (allowsMultiple)
        {
            // Every option correct would be a question nobody can get wrong, so at least one must be left incorrect.
            if (correctCount < 1 || correctCount >= optionCount)
                throw new InvalidQuestionError("A multiple-answer question needs at least one correct option and at least one incorrect option.");
        }
        else if (correctCount != 1)
        {
            throw new InvalidQuestionError("Exactly one option must be marked correct.");
        }
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
