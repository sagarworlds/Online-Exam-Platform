using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Notifications.Domain;

/// <summary>
/// One message in a signed-in account's in-app feed (FR-39). It is recorded only for an account, because the feed is what an account
/// holder reads: a guardian has no account and is told by e-mail alone, so no notice is ever recorded for one.
/// <para>
/// A notice is recorded once per recipient, kind and subject (one reminder per candidate per exam, one result notice per attempt). An
/// event that is announced again, by a run that repeats or by two runs at once, therefore adds nothing. The store enforces that uniqueness
/// too, so the rule holds even when the application's check races another request.
/// </para>
/// <para>
/// It keeps the exam's name and not the message text. Staff notes and scores stay where they were first written, so the feed does not
/// hold a second copy of free text that someone may later have to remove (NFR-6, purpose limitation).
/// </para>
/// </summary>
public sealed class InAppNotification : Entity
{
    /// <summary>The longest exam name a notice keeps; the column is this long.</summary>
    public const int MaxExamNameLength = 200;

    private InAppNotification(Guid id, Guid recipientUserId, NoticeKind kind, Guid subjectId, string? examName, DateTime createdAtUtc)
        : base(id)
    {
        RecipientUserId = recipientUserId;
        Kind = kind;
        SubjectId = subjectId;
        ExamName = examName;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>The account the notice is for.</summary>
    public Guid RecipientUserId { get; }

    /// <summary>The event the notice is about.</summary>
    public NoticeKind Kind { get; }

    /// <summary>The thing the event happened to (an invite, an attempt, a dispute, an exam, a score revision, according to the kind).</summary>
    public Guid SubjectId { get; }

    /// <summary>The exam's name at the time of the event, or null when the notice is not about an exam.</summary>
    public string? ExamName { get; }

    /// <summary>When the notice was recorded.</summary>
    public DateTime CreatedAtUtc { get; }

    /// <summary>When the recipient first marked it read, or null while it is unread.</summary>
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>Whether the recipient has read it.</summary>
    public bool IsRead => ReadAtUtc is not null;

    /// <summary>Records a notice that is unread.</summary>
    /// <param name="recipientUserId">The account it is for.</param>
    /// <param name="kind">The event it is about.</param>
    /// <param name="subjectId">The thing the event happened to.</param>
    /// <param name="examName">The exam's name, if the notice is about an exam; blank is stored as none, and a longer name is cut to the column's length.</param>
    /// <param name="nowUtc">When it is recorded.</param>
    /// <returns>The new, unread notice.</returns>
    /// <exception cref="ArgumentException">The recipient or the subject is the empty id, so the notice could never be found or matched.</exception>
    public static InAppNotification Create(Guid recipientUserId, NoticeKind kind, Guid subjectId, string? examName, DateTime nowUtc)
    {
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("A notice needs an account to be recorded for.", nameof(recipientUserId));
        if (subjectId == Guid.Empty)
            throw new ArgumentException("A notice needs the thing it is about.", nameof(subjectId));

        return new InAppNotification(Guid.NewGuid(), recipientUserId, kind, subjectId, ClipExamName(examName), nowUtc);
    }

    /// <summary>Marks the notice read. The first time is kept: a second read does not move it.</summary>
    /// <param name="nowUtc">When the recipient read it.</param>
    public void MarkRead(DateTime nowUtc) => ReadAtUtc ??= nowUtc;

    // A name that will not fit is cut rather than refused: the notice must still be recorded, and the cut name is still the exam's.
    private static string? ClipExamName(string? examName)
    {
        var trimmed = examName?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length <= MaxExamNameLength ? trimmed : trimmed[..MaxExamNameLength];
    }
}
