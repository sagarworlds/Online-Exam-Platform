using Microsoft.EntityFrameworkCore;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;
using ExamPlatform.Modules.Batch.Domain;
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
        // Tracked, with children loaded, on purpose: the aggregate's rules (duplicate/capacity/
        // validity checks) read its child collection, and handlers rely on change tracking to
        // INSERT new children; an explicit DbSet.Update would flag them Modified instead.
        return await context.Batches
            .Include(b => b.Members)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
    }

    public async Task<BatchAggregate> GetByIdOrThrowAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var batch = await GetByIdAsync(batchId, cancellationToken);
        if (batch == null)
            throw new InvalidOperationException($"Batch with ID {batchId} not found.");
        return batch;
    }

    public async Task<IReadOnlyList<BatchAggregate>> ListByExamAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        return await context.Batches
            .AsNoTracking()
            .Where(b => b.ExamId == examId)
            .ToListAsync(cancellationToken);
    }
}
