namespace ExamPlatform.Modules.Guardian.Contracts;

/// <summary>
/// Whether a candidate has a guardian who confirmed the link to them (exam-platform-requirements.md section 7, FR-43). Exam Runtime asks
/// this before a candidate under 18 starts an exam, through this Contracts project only, so it never reads Guardian's links itself.
/// </summary>
public interface IGuardianLinkDirectory
{
    /// <summary>
    /// Whether at least one guardian's link to the candidate is confirmed and still in force.
    /// </summary>
    /// <remarks>
    /// A link that was never confirmed, was revoked, or was removed does not count. Only the confirmed-and-current state of a link is read.
    /// </remarks>
    /// <param name="candidateId">The candidate whose guardian is asked about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasConfirmedGuardianAsync(Guid candidateId, CancellationToken cancellationToken);
}
