using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.Application.Ports;

/// Repository port for Batch aggregate.
public interface IBatchRepository
{
    void Add(BatchAggregate batch);

    /// <summary>Finds a batch with its members, ready to change; null when there is none.</summary>
    Task<BatchAggregate?> GetByIdAsync(Guid batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchAggregate>> ListByExamAsync(Guid examId, CancellationToken cancellationToken = default);
}
