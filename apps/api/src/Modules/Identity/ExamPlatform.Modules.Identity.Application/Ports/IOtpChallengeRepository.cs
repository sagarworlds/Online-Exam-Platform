using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Persistence port for <see cref="OtpChallenge"/> aggregates.</summary>
public interface IOtpChallengeRepository
{
    /// <summary>Loads a challenge by id, or null if none exists.</summary>
    /// <param name="id">The challenge's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OtpChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new challenge for insertion on the next unit-of-work commit.</summary>
    /// <param name="challenge">The challenge to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken);
}
