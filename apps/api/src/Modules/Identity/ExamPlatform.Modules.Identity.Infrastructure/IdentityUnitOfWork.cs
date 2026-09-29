using ExamPlatform.Modules.Identity.Application;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>EF Core-backed <see cref="IIdentityUnitOfWork"/>, wrapping <see cref="IdentityDbContext"/>.</summary>
public sealed class IdentityUnitOfWork(IdentityDbContext context) : IIdentityUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
