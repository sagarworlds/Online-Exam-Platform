using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One extra attempt an administrator allowed a candidate at an exam, on request. Everyone has one attempt; each grant adds
/// one (see <see cref="AttemptAllowance"/>). A row rather than a counter, so who allowed it, when and why stays on record.
/// </summary>
public sealed class ExtraAttemptGrant : Entity
{
    /// <summary>The longest reason that may be recorded.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>The exam.</summary>
    public Guid ExamId { get; private set; }

    /// <summary>The candidate who may sit it again; the signed-in user's id.</summary>
    public Guid CandidateId { get; private set; }

    /// <summary>Which grant this is for the candidate at the exam, from 1. Unique, so two administrators granting at once cannot both succeed.</summary>
    public int Number { get; private set; }

    /// <summary>The staff user who allowed it.</summary>
    public Guid GrantedByUserId { get; private set; }

    /// <summary>When it was allowed.</summary>
    public DateTime GrantedAtUtc { get; private set; }

    /// <summary>Why, in the administrator's words, if they said.</summary>
    public string? Reason { get; private set; }

    // For EF Core.
    private ExtraAttemptGrant() : base(Guid.Empty)
    {
    }

    private ExtraAttemptGrant(Guid examId, Guid candidateId, int number, Guid grantedByUserId, DateTime grantedAtUtc, string? reason)
        : base(Guid.NewGuid())
    {
        ExamId = examId;
        CandidateId = candidateId;
        Number = number;
        GrantedByUserId = grantedByUserId;
        GrantedAtUtc = grantedAtUtc;
        Reason = reason;
    }

    /// <summary>Records a grant.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The candidate.</param>
    /// <param name="number">Which grant this is for the candidate at the exam: the number already granted, plus one.</param>
    /// <param name="grantedByUserId">The staff user allowing it.</param>
    /// <param name="grantedAtUtc">The current instant.</param>
    /// <param name="reason">Why; optional. Surrounding whitespace is removed and a blank reason is stored as none.</param>
    /// <exception cref="InvalidAttemptError">The reason is longer than <see cref="MaxReasonLength"/>.</exception>
    public static ExtraAttemptGrant Create(Guid examId, Guid candidateId, int number, Guid grantedByUserId, DateTime grantedAtUtc, string? reason)
    {
        var trimmed = reason?.Trim();
        if (trimmed is { Length: > MaxReasonLength })
            throw new InvalidAttemptError($"The reason must be at most {MaxReasonLength} characters.");

        return new ExtraAttemptGrant(examId, candidateId, number, grantedByUserId, grantedAtUtc, string.IsNullOrEmpty(trimmed) ? null : trimmed);
    }
}
