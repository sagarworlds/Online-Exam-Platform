using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IRoleRepository"/>.</summary>
public sealed class RoleRepository(IdentityDbContext context) : IRoleRepository
{
    private IQueryable<Role> Loaded() => context.Roles.Include(r => r.Permissions);

    /// <inheritdoc />
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Loaded().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
        Loaded().FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
}
