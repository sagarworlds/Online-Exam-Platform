using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.Application.Ports;

/// Repository port for Batch aggregate.
public interface IBatchRepository
{
    void Add(BatchAggregate batch);
    Task<BatchAggregate?> GetByIdAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<BatchAggregate> GetByIdOrThrowAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatchAggregate>> ListByExamAsync(Guid examId, CancellationToken cancellationToken = default);
}
