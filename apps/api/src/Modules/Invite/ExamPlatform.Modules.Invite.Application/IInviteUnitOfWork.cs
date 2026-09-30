namespace ExamPlatform.Modules.Invite.Application;

/// Unit of Work for coordinating invite changes.
public interface IInviteUnitOfWork
{
    /// Persist all changes in a single transaction.
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
