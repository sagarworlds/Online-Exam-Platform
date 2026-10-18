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

    /// <summary>Whether any user, active or not, holds the named role.</summary>
    /// <param name="roleName">The role's unique name, such as <c>SuperAdmin</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> AnyInRoleAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>Which of the given e-mail addresses already belong to an account, without loading the accounts.</summary>
    /// <param name="emails">The addresses to look up, matched exactly as sign-in matches them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The addresses that have an account, as they were given.</returns>
    Task<IReadOnlySet<string>> ListExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken);

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

    /// <summary>Reads the dates of birth of the given users, without loading their roles or sessions.</summary>
    /// <param name="userIds">The users.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The date of birth of each user found; an id that matches no user is left out.</returns>
    Task<IReadOnlyDictionary<Guid, DateOnly>> ListDatesOfBirthAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Reads the name, e-mail address and phone number of the given users, without tracking and without loading their roles or sessions.</summary>
    /// <param name="userIds">The users.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The contact of each user found, with the address or number null for a user who has none; an id that matches no user is left out.</returns>
    Task<IReadOnlyDictionary<Guid, UserContact>> ListContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Lists the student accounts (see <see cref="User.IsStudent"/>), without tracking, for staff to browse.</summary>
    /// <param name="search">Part of the name, e-mail address or phone number to look for, without regard to case; null or blank lists everyone.</param>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The students ordered by name, then e-mail address, then id. Their roles and sessions are not loaded.</returns>
    Task<IReadOnlyList<User>> ListStudentsAsync(string? search, int take, CancellationToken cancellationToken);

    /// <summary>Lists the student accounts that hold any of the given e-mail addresses, compared without regard to case, without tracking.</summary>
    /// <param name="emails">The addresses to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The students found, ordered by name, then e-mail address, then id. An address nobody holds, or only a staff account holds, finds nothing.</returns>
    Task<IReadOnlyList<User>> ListStudentsByEmailAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken);

    /// <summary>Whether an account other than the given one holds an e-mail address, compared without regard to case.</summary>
    /// <param name="email">The address.</param>
    /// <param name="exceptUserId">The account that is changing to it, which does not count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> AnyOtherWithEmailAsync(string email, Guid exceptUserId, CancellationToken cancellationToken);

    /// <summary>Whether an account other than the given one holds a phone number, compared exactly as sign-in compares it.</summary>
    /// <param name="phoneNumber">The number.</param>
    /// <param name="exceptUserId">The account that is changing to it, which does not count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> AnyOtherWithPhoneAsync(string phoneNumber, Guid exceptUserId, CancellationToken cancellationToken);

    /// <summary>Begins tracking a new user for insertion on the next unit-of-work commit.</summary>
    /// <param name="user">The user to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(User user, CancellationToken cancellationToken);
}
