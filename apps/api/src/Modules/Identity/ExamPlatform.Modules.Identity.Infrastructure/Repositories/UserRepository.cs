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
    private IQueryable<User> Loaded() => context.Users
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
    public async Task AddAsync(User user, CancellationToken cancellationToken) =>
        await context.Users.AddAsync(user, cancellationToken);
}
