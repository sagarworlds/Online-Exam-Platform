using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// A candidate's request for one more attempt at an exam, once they have used the ones they hold. It only asks: nothing changes
/// until an administrator approves it, which is when an <see cref="ExtraAttemptGrant"/> is recorded. A candidate has at most one
/// pending request per exam, so asking twice cannot queue the same request twice.
/// </summary>
public sealed class AttemptRequest : Entity
{
    /// <summary>The longest candidate message, or administrator note, that may be recorded.</summary>
    public const int MaxTextLength = 500;

    /// <summary>The exam.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate who asked; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>Why they want another attempt, in their words, if they said.</summary>
    public string? Message { get; private set; }

    /// <summary>When they asked.</summary>
    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Whether it is waiting, approved or declined.</summary>
    public AttemptRequestStatus Status { get; private set; }

    /// <summary>The staff user who decided it, once decided.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>When it was decided.</summary>
    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>What the administrator said about declining, if they said anything; shown to the candidate.</summary>
    public string? DecisionNote { get; private set; }

    // For EF Core.
    private AttemptRequest() : base(Guid.Empty)
    {
    }

    private AttemptRequest(Guid examId, Guid candidateId, string? message, DateTime requestedAtUtc) : base(Guid.NewGuid())
    {
        ExamId = examId;
        CandidateId = candidateId;
        Message = message;
        RequestedAtUtc = requestedAtUtc;
        Status = AttemptRequestStatus.Pending;
    }

    /// <summary>Records a new, pending request.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate asking.</param>
    /// <param name="message">Why; optional. Surrounding whitespace is removed and a blank message is stored as none.</param>
    /// <param name="requestedAtUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">The message is longer than <see cref="MaxTextLength"/>.</exception>
    public static AttemptRequest Create(Guid examId, Guid candidateId, string? message, DateTime requestedAtUtc) =>
        new(examId, candidateId, Clean(message, "message"), requestedAtUtc);

    /// <summary>Marks the request approved. The caller records the grant in the same save, so the two never disagree.</summary>
    /// <param name="decidedByUserId">The staff user approving it.</param>
    /// <param name="decidedAtUtc">The current instant.</param>
    /// <exception cref="AttemptRequestNotPendingError">It was already decided.</exception>
    public void Approve(Guid decidedByUserId, DateTime decidedAtUtc)
    {
        EnsurePending();
        Decide(AttemptRequestStatus.Approved, decidedByUserId, decidedAtUtc, null);
    }

    /// <summary>Marks the request declined.</summary>
    /// <param name="decidedByUserId">The staff user declining it.</param>
    /// <param name="decidedAtUtc">The current instant.</param>
    /// <param name="note">A reason the candidate will see; optional.</param>
    /// <exception cref="AttemptRequestNotPendingError">It was already decided.</exception>
    /// <exception cref="InvalidAttemptError">The note is longer than <see cref="MaxTextLength"/>.</exception>
    public void Decline(Guid decidedByUserId, DateTime decidedAtUtc, string? note)
    {
        EnsurePending();
        Decide(AttemptRequestStatus.Declined, decidedByUserId, decidedAtUtc, Clean(note, "note"));
    }

    private void Decide(AttemptRequestStatus status, Guid decidedByUserId, DateTime decidedAtUtc, string? note)
    {
        Status = status;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = decidedAtUtc;
        DecisionNote = note;
    }

    // A decided request is a record of what was decided and by whom; deciding it again would rewrite that.
    private void EnsurePending()
    {
        if (Status != AttemptRequestStatus.Pending)
            throw new AttemptRequestNotPendingError();
    }

    private static string? Clean(string? text, string what)
    {
        var trimmed = text?.Trim();
        if (trimmed is { Length: > MaxTextLength })
            throw new InvalidAttemptError($"The {what} must be at most {MaxTextLength} characters.");

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
