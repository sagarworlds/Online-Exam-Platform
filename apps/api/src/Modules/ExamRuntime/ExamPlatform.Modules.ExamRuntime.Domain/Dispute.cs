using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// A candidate's dispute of the answer key of one question in one of their attempts (FR-31). It only asks: the question's key is
/// corrected, if at all, by staff through the question bank, and a correction settles every open dispute about that question at once
/// (<see cref="Accept"/>). Staff may instead leave the key as it is and say why (<see cref="Reject"/>). A candidate disputes a question
/// in an attempt once, so a settled dispute is final and the same complaint cannot be queued again.
/// </summary>
public sealed class Dispute : AggregateRoot
{
    /// <summary>The longest reason a candidate may give.</summary>
    public const int MaxReasonLength = 1000;

    /// <summary>The longest answer staff may give when leaving a key as it is.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>The attempt whose result is disputed.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The exam, kept here so staff can list disputes without opening each attempt.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate who disputed; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>The question-bank id of the question whose answer key is disputed.</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>Why the candidate thinks the key is wrong, in their words.</summary>
    public string Reason { get; private set; }

    /// <summary>When they raised it.</summary>
    public DateTime RaisedAtUtc { get; private set; }

    /// <summary>Whether it is waiting, accepted or rejected.</summary>
    public DisputeStatus Status { get; private set; }

    /// <summary>The staff user who settled it; null while open, and for an acceptance made outside a signed-in request.</summary>
    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>When it was settled.</summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// What the candidate is told: staff's explanation when they rejected it, or the reason given for the key correction that accepted it.
    /// </summary>
    public string? ResolutionNote { get; private set; }

    // For EF Core.
    private Dispute() : base(Guid.Empty) => Reason = null!;

    private Dispute(Guid attemptId, Guid examId, Guid candidateId, Guid questionId, string reason, DateTime raisedAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        ExamId = examId;
        CandidateId = candidateId;
        QuestionId = questionId;
        Reason = reason;
        RaisedAtUtc = raisedAtUtc;
        Status = DisputeStatus.Open;
    }

    /// <summary>Records a new, open dispute.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="examId">The attempt's exam.</param>
    /// <param name="candidateId">The candidate who owns the attempt.</param>
    /// <param name="questionId">The question disputed.</param>
    /// <param name="reason">Why the key is wrong; required, surrounding whitespace is removed.</param>
    /// <param name="raisedAtUtc">The current instant.</param>
    /// <exception cref="InvalidAttemptError">The reason is missing, or longer than <see cref="MaxReasonLength"/>.</exception>
    public static Dispute Raise(Guid attemptId, Guid examId, Guid candidateId, Guid questionId, string? reason, DateTime raisedAtUtc)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidAttemptError("Say why you think the answer key is wrong.");
        if (trimmed.Length > MaxReasonLength)
            throw new InvalidAttemptError($"The reason must be at most {MaxReasonLength} characters.");

        var dispute = new Dispute(attemptId, examId, candidateId, questionId, trimmed, raisedAtUtc);
        dispute.AddDomainEvent(new DisputeRaisedEvent(dispute.Id, attemptId, examId, candidateId, questionId));
        return dispute;
    }

    /// <summary>Marks the dispute accepted: the question's answer key was corrected and this candidate's result rescored under it.</summary>
    /// <param name="resolvedByUserId">The staff user who made the correction, when known.</param>
    /// <param name="resolvedAtUtc">The current instant.</param>
    /// <param name="correctionReason">The reason given for the correction; shown to the candidate.</param>
    /// <exception cref="DisputeNotOpenError">It was already settled.</exception>
    public void Accept(Guid? resolvedByUserId, DateTime resolvedAtUtc, string correctionReason)
    {
        Resolve(DisputeStatus.Accepted, resolvedByUserId, resolvedAtUtc, correctionReason);
    }

    /// <summary>Marks the dispute rejected: staff left the answer key as it is.</summary>
    /// <param name="resolvedByUserId">The staff user rejecting it.</param>
    /// <param name="resolvedAtUtc">The current instant.</param>
    /// <param name="note">Why the key stands; required, because the candidate is owed an answer. Surrounding whitespace is removed.</param>
    /// <exception cref="DisputeNotOpenError">It was already settled.</exception>
    /// <exception cref="InvalidAttemptError">The note is missing, or longer than <see cref="MaxNoteLength"/>.</exception>
    public void Reject(Guid resolvedByUserId, DateTime resolvedAtUtc, string? note)
    {
        // The state is checked first, so a second rejection is refused as already settled and not as a missing note.
        EnsureOpen();

        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidAttemptError("Say why the answer key stands; the candidate will see it.");
        if (trimmed.Length > MaxNoteLength)
            throw new InvalidAttemptError($"The note must be at most {MaxNoteLength} characters.");

        Resolve(DisputeStatus.Rejected, resolvedByUserId, resolvedAtUtc, trimmed);
    }

    private void Resolve(DisputeStatus status, Guid? resolvedByUserId, DateTime resolvedAtUtc, string note)
    {
        // A settled dispute is a record of what was decided and by whom; settling it again would rewrite that.
        EnsureOpen();

        Status = status;
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = resolvedAtUtc;
        ResolutionNote = note;
        AddDomainEvent(new DisputeResolvedEvent(Id, AttemptId, ExamId, CandidateId, QuestionId, status == DisputeStatus.Accepted));
    }

    private void EnsureOpen()
    {
        if (Status != DisputeStatus.Open)
            throw new DisputeNotOpenError();
    }
}
