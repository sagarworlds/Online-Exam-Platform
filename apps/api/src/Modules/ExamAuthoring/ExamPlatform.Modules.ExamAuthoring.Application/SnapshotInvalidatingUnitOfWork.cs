using ExamPlatform.Modules.ExamAuthoring.Application.Ports;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// An <see cref="IExamAuthoringUnitOfWork"/> that drops the cached exam snapshots once the save it wraps has finished. Every change to an exam
/// goes through a unit of work, so on the host that made the change the next read sees it at once.
/// </summary>
/// <remarks>
/// The cache is cleared after the save, not before: a read that lands between a clear and the save would otherwise store the old exam under
/// the new generation and serve it until it expires. It is cleared whether or not the save succeeded: a failed save may still have written
/// part of a change, and a cache that is cleared too often only costs a read.
/// </remarks>
public sealed class SnapshotInvalidatingUnitOfWork(IExamAuthoringUnitOfWork inner, IExamSnapshotCache snapshots) : IExamAuthoringUnitOfWork
{
    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await inner.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            snapshots.Clear();
        }
    }
}
