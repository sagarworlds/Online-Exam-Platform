using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Infrastructure;

/// Unit of Work implementation for Batch module using EF Core.
public class BatchUnitOfWork(BatchDbContext context) : IBatchUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch domain events before saving (if needed)
        // For now, EF Core will handle event persistence via change tracking
        await context.SaveChangesAsync(cancellationToken);
    }
}
