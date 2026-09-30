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
        return await context.Batches
            .AsNoTracking()
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

    public void Update(BatchAggregate batch)
    {
        context.Batches.Update(batch);
    }
}
