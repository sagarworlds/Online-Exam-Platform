using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// A record of an <see cref="Attempt"/>'s score changing after it was first submitted (FR-31), most often because staff
/// corrected a question's answer key. Kept so a candidate's review can show that their result changed, when, and why,
/// rather than the new score silently replacing the one they already saw.
/// </summary>
public sealed class AttemptResultRevision : Entity
{
    /// <summary>The attempt this revision belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The score before this revision.</summary>
    public decimal PreviousScore { get; private set; }

    /// <summary>The marks available before this revision.</summary>
    public decimal PreviousMaxScore { get; private set; }

    /// <summary>The score after this revision.</summary>
    public decimal NewScore { get; private set; }

    /// <summary>The marks available after this revision.</summary>
    public decimal NewMaxScore { get; private set; }

    /// <summary>Why the score changed, as given by whoever made the correction that caused it.</summary>
    public string Reason { get; private set; }

    /// <summary>When the revision happened.</summary>
    public DateTime RevisedAtUtc { get; private set; }

    // For EF Core.
    private AttemptResultRevision() : base(Guid.Empty) => Reason = null!;

    internal AttemptResultRevision(
        Guid attemptId, decimal previousScore, decimal previousMaxScore, decimal newScore, decimal newMaxScore,
        string reason, DateTime revisedAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        PreviousScore = previousScore;
        PreviousMaxScore = previousMaxScore;
        NewScore = newScore;
        NewMaxScore = newMaxScore;
        Reason = reason;
        RevisedAtUtc = revisedAtUtc;
    }
}
