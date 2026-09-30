using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

public sealed class InvalidBatchConfigError(string message) : DomainException(message)
{
    public override string ErrorCode => "invalid_batch_config";
    public override int HttpStatusCode => 400;
}
