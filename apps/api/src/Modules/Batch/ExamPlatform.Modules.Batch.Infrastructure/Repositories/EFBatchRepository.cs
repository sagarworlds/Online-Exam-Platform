using Microsoft.EntityFrameworkCore;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;
using ExamPlatform.Modules.Batch.Application.Ports;

namespace ExamPlatform.Modules.Batch.Infrastructure.Repositories;

/// EF Core implementation of IBatchRepository.
public class EFBatchRepository(BatchDbContext context) : IBatchRepository
{
    public void Add(BatchAggregate batch)
    {
        context.Batches.Add(batch);
    }

    public async Task<BatchAggregate?> GetByIdAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        // Tracked on purpose: handlers mutate the aggregate and rely on the unit of work's change
        // tracking to INSERT new children; an explicit DbSet.Update would flag them Modified instead.
        return await Loaded().FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
    }

    public async Task<IReadOnlyList<BatchAggregate>> ListByExamAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        return await Loaded()
            .AsNoTracking()
            .Where(b => b.ExamId == examId)
            .ToListAsync(cancellationToken);
    }

    // Without its members every rule sees an empty batch: nothing is ever a duplicate, the capacity
    // never fills, and a batch with seats refuses to activate for having none.
    private IQueryable<BatchAggregate> Loaded() => context.Batches.Include(b => b.Members);
}
