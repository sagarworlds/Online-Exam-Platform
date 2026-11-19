using ExamPlatform.Modules.ExamAuthoring.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure;

/// <summary>
/// EF Core-backed <see cref="IExamAuthoringUnitOfWork"/>, wrapping <see cref="ExamAuthoringDbContext"/>. When it has saved, the cached exam
/// snapshots are dropped (see <see cref="ExamSnapshotCache"/>), whether or not the save succeeded: a failed save may still have written part of
/// a change, and a cache that is cleared too often only costs a read.
/// </summary>
public sealed class ExamAuthoringUnitOfWork(ExamAuthoringDbContext context, ExamSnapshotCache? snapshots = null) : IExamAuthoringUnitOfWork
{
    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            snapshots?.Clear();
        }
    }
}
