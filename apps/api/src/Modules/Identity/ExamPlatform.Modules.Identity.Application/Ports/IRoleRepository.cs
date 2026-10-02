using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Persistence port for <see cref="Role"/> reference data.</summary>
public interface IRoleRepository
{
    /// <summary>Loads a role by id, or null if none exists.</summary>
    /// <param name="id">The role's identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads a role by its unique name, or null if none exists.</summary>
    /// <param name="name">The role name (e.g. "Candidate", "SuperAdmin").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken);

    /// <summary>Loads every role with its permissions, ordered by name.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);
}
