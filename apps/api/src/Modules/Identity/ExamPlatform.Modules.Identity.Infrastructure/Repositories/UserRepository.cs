using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IUserRepository"/>.</summary>
public sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    // ThenInclude(Permissions) matters: without it, every role loads with an empty
    // Permissions collection, so RBAC and JwtTokenGenerator's "perm" claims would
    // silently see no permissions at all for any user, however their roles are configured.
    // Split, since Roles->Permissions and Sessions are independent collections: joined in one
    // query they'd return the cartesian product of a user's permissions and their sessions,
    // on the hot path every sign-in and token validation runs.
    private IQueryable<User> Loaded() => context.Users
        .AsSplitQuery()
        .Include(u => u.Roles).ThenInclude(r => r.Permissions)
        .Include(u => u.Sessions);

    /// <inheritdoc />
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Loaded().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        Loaded().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    /// <inheritdoc />
    public Task<User?> GetByPhoneAsync(string phoneNumber, CancellationToken cancellationToken) =>
        Loaded().FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListActiveEmailsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && u.Email != null
                && u.Roles.Any(r => r.Permissions.Any(p => p.Code == permissionCode)))
            .Select(u => u.Email!)
            .Distinct()
            .OrderBy(email => email)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid UserId, string? Email)>> ListActiveWithPermissionAsync(string permissionCode, CancellationToken cancellationToken)
    {
        // The projection is anonymous because EF cannot translate a tuple; the tuples are built once the rows are in memory.
        var rows = await context.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => r.Permissions.Any(p => p.Code == permissionCode)))
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Id, r.Email)).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await context.Users.AddAsync(user, cancellationToken);
}
