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

    /// <summary>Lists the e-mail addresses of the active users who hold a permission through any of their roles.</summary>
    /// <param name="permissionCode">The permission code, such as <c>exam.manage</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each address once, in a stable order; users who are not active or have no e-mail address are left out.</returns>
    Task<IReadOnlyList<string>> ListActiveEmailsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken);

    /// <summary>Lists the active users who hold a permission through any of their roles, with their e-mail address when they have one.</summary>
    /// <param name="permissionCode">The permission code, such as <c>exam.manage</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Each user once, ordered by address then id; the address is null for a user who has none.</returns>
    Task<IReadOnlyList<(Guid UserId, string? Email)>> ListActiveWithPermissionAsync(string permissionCode, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new user for insertion on the next unit-of-work commit.</summary>
    /// <param name="user">The user to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(User user, CancellationToken cancellationToken);
}
