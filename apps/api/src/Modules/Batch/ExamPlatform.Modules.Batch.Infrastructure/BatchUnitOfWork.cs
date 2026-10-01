using ExamPlatform.Modules.Batch.Application;

namespace ExamPlatform.Modules.Batch.Infrastructure;

/// <summary>EF Core-backed <see cref="IBatchUnitOfWork"/>, wrapping <see cref="BatchDbContext"/>.</summary>
public sealed class BatchUnitOfWork(BatchDbContext context) : IBatchUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
