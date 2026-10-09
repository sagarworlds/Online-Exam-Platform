using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// The ExamRuntime module's unit of work. A distinct interface per module (see
/// <c>Identity.Application.IIdentityUnitOfWork</c> for the rationale) so DI cannot
/// resolve another module's <c>DbContext</c>-backed implementation.
/// </summary>
public interface IExamRuntimeUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Takes a lock on one attempt, held until <see cref="IAttemptLock.CompleteAsync"/> (which commits what was saved meanwhile) or
    /// disposal (which rolls it back), so requests that change the same attempt take turns instead of working from a stale copy of it.
    /// </summary>
    /// <remarks>
    /// Saving an answer and submitting the attempt each load the attempt, change it and save. Without this, a submit that loaded the
    /// attempt a moment before an answer was committed scores without that answer, and nothing notices, because saving an answer does
    /// not touch the attempt's own row. Take the lock <b>before</b> loading the attempt, so what is loaded is what the previous holder committed.
    /// </remarks>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="exclusive">
    /// True to end or otherwise rewrite the attempt (submit): waits for everyone holding it, and holds everyone else off. False to add to it
    /// (save an answer): any number of these run together, but not with an exclusive one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IAttemptLock> LockAttemptAsync(Guid attemptId, bool exclusive, CancellationToken cancellationToken);
}

/// <summary>A lock on one attempt; see <see cref="IExamRuntimeUnitOfWork.LockAttemptAsync"/>.</summary>
public interface IAttemptLock : IAsyncDisposable
{
    /// <summary>Commits what was saved while the lock was held, and releases it. Without this, disposal undoes it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAsync(CancellationToken cancellationToken);
}
