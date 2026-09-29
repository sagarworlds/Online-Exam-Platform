using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Persistence port for <see cref="PasswordResetToken"/> aggregates.</summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>Loads a token by id, or null if none exists.</summary>
    /// <param name="id">The token's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PasswordResetToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new token for insertion on the next unit-of-work commit.</summary>
    /// <param name="token">The token to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken);
}
