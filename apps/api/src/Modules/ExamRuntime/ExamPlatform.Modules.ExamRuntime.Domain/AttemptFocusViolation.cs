using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// One time a candidate left the exam page during an <see cref="Attempt"/> (FR-22): when the server heard about it and how it
/// happened. Kept as rows rather than a counter so a reviewer, or a dispute, can see what the candidate was told they had done.
/// </summary>
/// <remarks>
/// The time is the server's, taken when the report arrived, never the browser's: a candidate's clock is theirs to set. The page
/// can be kept from reporting at all (a browser that ignores the page's scripts), so a count here is evidence of what was seen,
/// not proof of what did not happen; nothing but the attempt's own rules reads it as a verdict.
/// </remarks>
public sealed class AttemptFocusViolation : Entity
{
    /// <summary>The attempt this belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>How the candidate left the page.</summary>
    public FocusViolationKind Kind { get; private set; }

    /// <summary>When the server heard about it.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    // For EF Core.
    private AttemptFocusViolation() : base(Guid.Empty)
    {
    }

    internal AttemptFocusViolation(Guid attemptId, FocusViolationKind kind, DateTime occurredAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        Kind = kind;
        OccurredAtUtc = occurredAtUtc;
    }
}
