namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Commits changes tracked by a module's repositories in one transaction.
/// Each module has its own implementation wrapping its own <c>DbContext</c> —
/// there is no cross-module transaction, matching the "modules never reach
/// into each other's tables" rule (ADR 0001).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Persists all tracked changes.</summary>
    /// <param name="cancellationToken">Cancellation token for the save.</param>
    /// <returns>The number of state entries written to the store.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
