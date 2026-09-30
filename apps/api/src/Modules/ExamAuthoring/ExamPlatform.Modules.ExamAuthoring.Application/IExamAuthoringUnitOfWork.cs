namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// Unit of Work for coordinating exam authoring changes (transaction handling, domain event dispatch).
public interface IExamAuthoringUnitOfWork
{
    /// Persist all changes (aggregates, domain events) in a single transaction.
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
