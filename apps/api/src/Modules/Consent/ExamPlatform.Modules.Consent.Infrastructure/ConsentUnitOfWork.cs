using ExamPlatform.Modules.Consent.Application;

namespace ExamPlatform.Modules.Consent.Infrastructure;

/// <summary>EF Core-backed <see cref="IConsentUnitOfWork"/>, wrapping <see cref="ConsentDbContext"/>.</summary>
public sealed class ConsentUnitOfWork(ConsentDbContext context) : IConsentUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
