using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
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

    // lower() on both sides and not ILIKE: an address may hold "_", which ILIKE reads as a wildcard.
    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListByEmailIgnoreCaseAsync(string email, CancellationToken cancellationToken)
    {
        var wanted = email.Trim().ToLowerInvariant();
        return await Loaded().Where(u => u.Email != null && u.Email.ToLower() == wanted).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> AnyInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(u => u.Roles.Any(r => r.Name == roleName), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> ListExistingEmailsAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken) =>
        (await context.Users.AsNoTracking()
            .Where(u => u.Email != null && emails.Contains(u.Email))
            .Select(u => u.Email!)
            .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

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
    public async Task<IReadOnlyDictionary<Guid, DateOnly>> ListDatesOfBirthAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        // A projection rather than Loaded(): an age check needs one column, and loading roles and sessions for every attempt would be wasted.
        var rows = await context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DateOfBirth })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.DateOfBirth);
    }

    // Holds the Candidate role and no role of any other kind but Guardian: the same rule as User.IsStudent, written as a query so the list
    // is filtered by the database and not by loading every account.
    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListStudentsAsync(string? search, int take, CancellationToken cancellationToken)
    {
        var query = context.Users.AsNoTracking()
            .Where(u => u.Roles.Any(r => r.Name == RbacCatalog.RoleNames.Candidate)
                && !u.Roles.Any(r => r.Name != RbacCatalog.RoleNames.Candidate && r.Name != RbacCatalog.RoleNames.Guardian));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(u => EF.Functions.ILike(u.DisplayName, pattern, "\\")
                || (u.Email != null && EF.Functions.ILike(u.Email, pattern, "\\"))
                || (u.PhoneNumber != null && EF.Functions.ILike(u.PhoneNumber, pattern, "\\")));
        }

        return await query.OrderBy(u => u.DisplayName).ThenBy(u => u.Email).ThenBy(u => u.Id).Take(take).ToListAsync(cancellationToken);
    }

    // lower() on both sides and not ILIKE, for the reason given below. The addresses are lower-cased here so the database compares like with like.
    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListStudentsByEmailAsync(IReadOnlyCollection<string> emails, CancellationToken cancellationToken)
    {
        var wanted = emails.Select(email => email.ToLowerInvariant()).Distinct().ToList();

        return await context.Users.AsNoTracking()
            .Where(u => u.Email != null && wanted.Contains(u.Email.ToLower())
                && u.Roles.Any(r => r.Name == RbacCatalog.RoleNames.Candidate)
                && !u.Roles.Any(r => r.Name != RbacCatalog.RoleNames.Candidate && r.Name != RbacCatalog.RoleNames.Guardian))
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Email).ThenBy(u => u.Id)
            .ToListAsync(cancellationToken);
    }

    // lower() on both sides and not ILIKE: an address may hold "_", which ILIKE reads as a wildcard.
    /// <inheritdoc />
    public Task<bool> AnyOtherWithEmailAsync(string email, Guid exceptUserId, CancellationToken cancellationToken)
    {
        var wanted = email.ToLowerInvariant();
        return context.Users.AsNoTracking().AnyAsync(u => u.Id != exceptUserId && u.Email != null && u.Email.ToLower() == wanted, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> AnyOtherWithPhoneAsync(string phoneNumber, Guid exceptUserId, CancellationToken cancellationToken) =>
        context.Users.AsNoTracking().AnyAsync(u => u.Id != exceptUserId && u.PhoneNumber == phoneNumber, cancellationToken);

    // A name such as "a_b" must match literally, so LIKE's wildcards are escaped.
    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <inheritdoc />
    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await context.Users.AddAsync(user, cancellationToken);
}
