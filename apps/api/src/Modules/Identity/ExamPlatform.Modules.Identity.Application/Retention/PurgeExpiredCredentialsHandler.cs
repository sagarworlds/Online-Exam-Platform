using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Application.Retention;

/// <summary>
/// Removes sign-in credentials that have been expired for longer than the configured grace period (FR-47): one-time codes, password-reset
/// links and sessions.
/// </summary>
/// <remarks>
/// Consent records, the audit log and accounts are not credentials, so they are never touched here; each has its own rule in the data map.
/// The grace period is measured from each credential's own expiry, not from when it was issued.
/// </remarks>
public sealed class PurgeExpiredCredentialsHandler(
    IExpiredCredentialStore store, Clock clock, IOptions<CredentialRetentionOptions> options)
{
    /// <summary>Deletes what has expired before the grace period's cut-off.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows of each kind were removed.</returns>
    public Task<CredentialPurgeResult> HandleAsync(CancellationToken cancellationToken)
    {
        var cutoffUtc = clock.UtcNow.AddDays(-options.Value.GraceDays);
        return store.DeleteExpiredAsync(cutoffUtc, cancellationToken);
    }
}
