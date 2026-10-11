using ExamPlatform.Modules.Identity.Application.Retention;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>
/// Deletes the sign-in credentials that expired before a cut-off (FR-47). Kept as a port so the rule for which rows count as expired is
/// in one place, and the database work stays in Infrastructure.
/// </summary>
public interface IExpiredCredentialStore
{
    /// <summary>
    /// Deletes every one-time code, password-reset token and session whose expiry is before <paramref name="cutoffUtc"/>.
    /// </summary>
    /// <remarks>
    /// Each kind is deleted on its own, so a failure part-way leaves the earlier kinds deleted; the next sweep removes whatever is left.
    /// Only rows past their own expiry are matched, so an active credential is never deleted.
    /// </remarks>
    /// <param name="cutoffUtc">The instant before which a credential counts as expired for good.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows of each kind were deleted.</returns>
    Task<CredentialPurgeResult> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
