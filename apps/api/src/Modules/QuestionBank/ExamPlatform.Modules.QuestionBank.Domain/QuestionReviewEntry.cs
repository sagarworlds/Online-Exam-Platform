using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>What a <see cref="QuestionReviewEntry"/> records.</summary>
public enum QuestionReviewEntryKind
{
    /// <summary>A comment, which changes nothing about the question's status.</summary>
    Commented = 1,

    /// <summary>The question was put forward for review.</summary>
    Submitted = 2,

    /// <summary>A reviewer approved it.</summary>
    Approved = 3,

    /// <summary>A reviewer sent it back to its author with a reason.</summary>
    ChangesRequested = 4,

    /// <summary>The question was taken out of use.</summary>
    Retired = 5,

    /// <summary>A retired question was brought back as a draft.</summary>
    Restored = 6,
}

/// <summary>
/// One line of a question's review thread (FR-8): a comment, or a step of the workflow with the comment that came with it. Entries are
/// only ever added, so the thread is the record of who approved what and why it was sent back.
/// </summary>
public sealed class QuestionReviewEntry : Entity
{
    /// <summary>The longest comment, after trimming.</summary>
    public const int MaxCommentLength = 2000;

    /// <summary>The longest name kept for the person who wrote an entry.</summary>
    public const int MaxByLabelLength = 254;

    /// <summary>The question this is about.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>What happened.</summary>
    public QuestionReviewEntryKind Kind { get; private set; }

    /// <summary>The staff member who did it.</summary>
    public Guid ByUserId { get; private set; }

    /// <summary>
    /// Who that was, as it should be shown: their email address when the token carried one. Kept on the entry because the question bank
    /// does not know users, and a thread has to stay readable if the account changes.
    /// </summary>
    public string ByLabel { get; private set; }

    /// <summary>The comment that came with it; empty when none was given.</summary>
    public string Comment { get; private set; }

    /// <summary>The version of the question that was current at the time, so an approval names what was approved (FR-7).</summary>
    public int VersionNumber { get; private set; }

    /// <summary>The status the question had afterwards.</summary>
    public QuestionStatus StatusAfter { get; private set; }

    /// <summary>When it happened.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    // For EF Core.
    private QuestionReviewEntry() : base(Guid.Empty)
    {
        ByLabel = null!;
        Comment = null!;
    }

    internal QuestionReviewEntry(
        Guid questionId, QuestionReviewEntryKind kind, Guid byUserId, string? byLabel, string comment, int versionNumber,
        QuestionStatus statusAfter, DateTime createdAtUtc) : base(Guid.NewGuid())
    {
        QuestionId = questionId;
        Kind = kind;
        ByUserId = byUserId;
        ByLabel = string.IsNullOrWhiteSpace(byLabel) ? "staff" : byLabel.Trim()[..Math.Min(byLabel.Trim().Length, MaxByLabelLength)];
        Comment = comment;
        VersionNumber = versionNumber;
        StatusAfter = statusAfter;
        CreatedAtUtc = createdAtUtc;
    }
}
