using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Persistence port for <see cref="User"/> aggregates, including their sessions and roles.</summary>
public interface IUserRepository
{
    /// <summary>Loads a user by id, including roles and sessions, or null if none exists.</summary>
    /// <param name="id">The user's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads a user by email address, or null if none exists.</summary>
    /// <param name="email">The email address to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Loads a user by phone number, or null if none exists.</summary>
    /// <param name="phoneNumber">The phone number to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new user for insertion on the next unit-of-work commit.</summary>
    /// <param name="user">The user to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(User user, CancellationToken cancellationToken);
}
