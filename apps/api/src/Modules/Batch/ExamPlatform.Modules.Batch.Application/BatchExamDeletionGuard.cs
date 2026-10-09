using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;

namespace ExamPlatform.Modules.Batch.Application;

/// <summary>
/// Objects to an exam being deleted while a batch is assigned to it. A batch holds the exam's id and no foreign key can
/// reach across modules, so deleting the exam would leave the batch pointing at nothing.
/// </summary>
public sealed class BatchExamDeletionGuard(IBatchRepository repository) : IExamDeletionGuard
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindObjectionsAsync(Guid examId, CancellationToken cancellationToken) =>
        (await repository.ListByExamAsync(examId, cancellationToken)).Count > 0
            ? ["A batch is assigned to it."]
            : [];
}
