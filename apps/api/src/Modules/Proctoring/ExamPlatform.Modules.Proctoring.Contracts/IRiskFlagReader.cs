namespace ExamPlatform.Modules.Proctoring.Contracts;

/// <summary>
/// What other modules may ask about the risk review of an attempt (FR-27, ADR 0001). Result release can use it to hold an attempt whose
/// flag is still waiting for a reviewer. It answers yes or no and never tells the caller what the flag is about.
/// Consumed through this Contracts project only.
/// </summary>
public interface IRiskFlagReader
{
    /// <summary>Whether the attempt has a flag that no reviewer has decided on yet.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when a flagged assessment of the attempt is still open; false when there is none, or the reviewer has decided.</returns>
    Task<bool> IsAwaitingReviewAsync(Guid attemptId, CancellationToken cancellationToken);
}
