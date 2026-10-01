using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Persistence port for <see cref="PasswordResetToken"/> aggregates.</summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>Loads a token by id, or null if none exists.</summary>
    /// <param name="id">The token's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PasswordResetToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads, for update, every token of a user that could still be used: not consumed, not
    /// revoked, and not yet expired at <paramref name="nowUtc"/>.
    /// </summary>
    /// <param name="userId">The user the tokens were issued for.</param>
    /// <param name="nowUtc">
    /// The current instant; a token expiring before it is not outstanding, while one expiring
    /// exactly at it still is, matching <see cref="PasswordResetToken.IsUsable"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PasswordResetToken>> GetOutstandingForUserAsync(
        Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes, at <paramref name="nowUtc"/>, every token of a user that is outstanding in the
    /// sense of <see cref="GetOutstandingForUserAsync"/>, and returns how many it revoked.
    /// Unlike the other members this writes straight away, as one set-based update, not on
    /// the next unit-of-work commit, so a token that a parallel call has just revoked or
    /// consumed is skipped instead of failing the caller with a concurrency conflict.
    /// </summary>
    /// <param name="userId">The user the tokens were issued for.</param>
    /// <param name="nowUtc">The current instant, recorded as each token's revocation time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> RevokeOutstandingForUserAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new token for insertion on the next unit-of-work commit.</summary>
    /// <param name="token">The token to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken);
}
