namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>An option as a <see cref="QuestionVersion"/> snapshot keeps it: a copy taken at the time, never updated afterward.</summary>
/// <param name="OptionId">The id of the live <see cref="QuestionOption"/> this was copied from.</param>
/// <param name="Text">The option text at that time.</param>
/// <param name="IsCorrect">Whether the option was correct at that time.</param>
/// <param name="Order">Its display order at that time.</param>
/// <param name="IsPinned">Whether it kept its place when options were shuffled, at that time.</param>
public sealed record QuestionVersionOption(Guid OptionId, string Text, bool IsCorrect, int Order, bool IsPinned);

/// <summary>
/// An immutable snapshot of a <see cref="Question"/>'s gradable content (FR-7): its text, options and answer key as
/// they were between one accepted change and the next. <see cref="Question"/> appends one every time
/// <see cref="Question.Create"/>, <see cref="Question.Revise"/> or <see cref="Question.CorrectAnswerKey"/> succeeds;
/// none already taken is ever edited or removed, so an exam or attempt built while an earlier version was current
/// keeps reading the same content no matter what the author does next.
/// </summary>
public sealed class QuestionVersion
{
    /// <summary>The snapshot's own id.</summary>
    public Guid Id { get; private set; }

    /// <summary>The question this is a version of.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>1 for the version a question is created with, incrementing by one on every version taken after it.</summary>
    public int VersionNumber { get; private set; }

    /// <summary>The question text at the time, as sanitized HTML.</summary>
    public string Text { get; private set; } = null!;

    /// <summary>Whether more than one option could be correct at the time.</summary>
    public bool AllowsMultiple { get; private set; }

    /// <summary>The options as they were, in their display order at the time.</summary>
    public IReadOnlyList<QuestionVersionOption> Options { get; private set; } = [];

    /// <summary>Whether the candidate typed the answer at the time (a text question) rather than choosing an option.</summary>
    public bool IsTextAnswer { get; private set; }

    /// <summary>The accepted answers at the time, for a text question; empty for a multiple-choice one.</summary>
    public string[] AcceptedAnswers { get; private set; } = [];

    /// <summary>When this became the current version.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    // For EF Core.
    private QuestionVersion() { }

    internal QuestionVersion(
        Guid questionId, int versionNumber, string text, bool allowsMultiple, IReadOnlyList<QuestionVersionOption> options, DateTime createdAtUtc,
        bool isTextAnswer = false, string[]? acceptedAnswers = null)
    {
        Id = Guid.NewGuid();
        QuestionId = questionId;
        VersionNumber = versionNumber;
        Text = text;
        AllowsMultiple = allowsMultiple;
        Options = options;
        CreatedAtUtc = createdAtUtc;
        IsTextAnswer = isTextAnswer;
        AcceptedAnswers = acceptedAnswers ?? [];
    }
}
