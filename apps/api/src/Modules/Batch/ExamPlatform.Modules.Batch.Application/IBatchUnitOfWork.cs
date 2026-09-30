namespace ExamPlatform.Modules.Batch.Application;

/// Unit of Work for coordinating batch changes.
public interface IBatchUnitOfWork
{
    /// Persist all changes in a single transaction.
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
