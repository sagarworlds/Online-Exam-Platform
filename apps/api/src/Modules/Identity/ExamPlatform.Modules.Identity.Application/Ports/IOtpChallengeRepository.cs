using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Persistence port for <see cref="OtpChallenge"/> aggregates.</summary>
public interface IOtpChallengeRepository
{
    /// <summary>Loads a challenge by id, or null if none exists.</summary>
    /// <param name="id">The challenge's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OtpChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads, for update, every challenge for a destination and purpose that could still
    /// be verified: not consumed, not superseded, and not yet expired at <paramref name="nowUtc"/>.
    /// </summary>
    /// <param name="destination">The email address or phone number the challenges were sent to.</param>
    /// <param name="purpose">What the challenges authorize.</param>
    /// <param name="nowUtc">
    /// The current instant; a challenge expiring before it is not outstanding, while one expiring
    /// exactly at it still is, matching <see cref="OtpChallenge.Verify"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OtpChallenge>> GetOutstandingAsync(
        string destination, OtpPurpose purpose, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the challenges whose code staff may still read: one is kept, not yet consumed,
    /// superseded, expired or locked out. Newest first.
    /// </summary>
    /// <param name="destinationContains">Only destinations containing this text (case-insensitive), or null for all.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="take">The most challenges to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OtpChallenge>> ListRevealableAsync(
        string? destinationContains, DateTime nowUtc, int take, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new challenge for insertion on the next unit-of-work commit.</summary>
    /// <param name="challenge">The challenge to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken);
}
