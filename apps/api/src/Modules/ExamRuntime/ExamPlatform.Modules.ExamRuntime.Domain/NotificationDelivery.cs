using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// The record that one e-mail of a kind about one subject was sent to one person, or tried and failed (FR-39). It is what makes the
/// notification run safe to repeat: the run looks at what is due every few minutes, and this says which of it was already done, so a
/// restart, an overlapping run or a second trigger never sends the same message twice. A message that fails is retried a few times, a
/// while apart, and then given up on, so an address that bounces is not tried for ever.
/// </summary>
public sealed class NotificationDelivery : Entity
{
    /// <summary>How many times a message is tried before it is given up on.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How long after a failed try the next one waits, so a mail outage is not hammered once a minute.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    /// <summary>Which e-mail this is.</summary>
    public NotificationKind Kind { get; private set; }

    /// <summary>What it is about: the exam for a reminder, the attempt for a released result, the revision for a revised score.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>The candidate it was for; the signed-in user's id.</summary>
    public Guid RecipientId { get; private set; }

    /// <summary>How many times it was tried.</summary>
    public int Attempts { get; private set; }

    /// <summary>When it was last tried.</summary>
    public DateTime? LastAttemptAtUtc { get; private set; }

    /// <summary>When the mail server took it; null while it has not been sent.</summary>
    public DateTime? SentAtUtc { get; private set; }

    // For EF Core.
    private NotificationDelivery() : base(Guid.Empty)
    {
    }

    private NotificationDelivery(NotificationKind kind, Guid subjectId, Guid recipientId) : base(Guid.NewGuid())
    {
        Kind = kind;
        SubjectId = subjectId;
        RecipientId = recipientId;
    }

    /// <summary>Starts the record of a message that has not been tried yet.</summary>
    /// <param name="kind">Which e-mail.</param>
    /// <param name="subjectId">What it is about.</param>
    /// <param name="recipientId">The candidate it is for.</param>
    public static NotificationDelivery Start(NotificationKind kind, Guid subjectId, Guid recipientId) => new(kind, subjectId, recipientId);

    /// <summary>Whether there is nothing more to do: it was sent, or was tried as often as it ever will be.</summary>
    public bool IsSettled => SentAtUtc is not null || Attempts >= MaxAttempts;

    /// <summary>Whether it should be tried now: not settled, and not tried so recently that a retry would be too soon.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public bool IsDueAt(DateTime nowUtc) => !IsSettled && (LastAttemptAtUtc is not { } last || nowUtc - last >= RetryAfter);

    /// <summary>Records one try.</summary>
    /// <param name="sent">Whether the mail server took the message.</param>
    /// <param name="nowUtc">The current instant.</param>
    public void RecordAttempt(bool sent, DateTime nowUtc)
    {
        Attempts++;
        LastAttemptAtUtc = nowUtc;
        if (sent)
            SentAtUtc = nowUtc;
    }
}
