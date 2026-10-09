using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// A warning an administrator sent to a candidate during an <see cref="Attempt"/> (FR-29): what it said, who sent it and when.
/// Kept as rows so the candidate's page can show every warning, and a dispute can show what they were told.
/// </summary>
public sealed class AttemptWarning : Entity
{
    /// <summary>The longest warning an administrator can send.</summary>
    public const int MaxMessageLength = 500;

    /// <summary>The attempt this belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>What the administrator said.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>The administrator who sent it, taken from their token.</summary>
    public Guid IssuedByUserId { get; private set; }

    /// <summary>When it was sent, by the server's clock.</summary>
    public DateTime IssuedAtUtc { get; private set; }

    // For EF Core.
    private AttemptWarning() : base(Guid.Empty)
    {
    }

    internal AttemptWarning(Guid attemptId, string message, Guid issuedByUserId, DateTime issuedAtUtc) : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        Message = message;
        IssuedByUserId = issuedByUserId;
        IssuedAtUtc = issuedAtUtc;
    }
}
