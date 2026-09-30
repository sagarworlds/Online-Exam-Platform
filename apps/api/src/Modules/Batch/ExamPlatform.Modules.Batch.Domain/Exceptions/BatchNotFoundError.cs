using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

public sealed class BatchNotFoundError(Guid batchId) : DomainException($"Batch '{batchId}' not found.")
{
    public override string ErrorCode => "batch_not_found";
    public override int HttpStatusCode => 404;
}
