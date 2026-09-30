namespace ExamPlatform.Modules.Guardian.Application;

/// Unit of Work for coordinating guardian changes.
public interface IGuardianUnitOfWork
{
    /// Persist all changes in a single transaction.
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
