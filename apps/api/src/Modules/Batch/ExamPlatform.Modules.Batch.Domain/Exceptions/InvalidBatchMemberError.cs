using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

/// <summary>A batch member's e-mail address or phone number is not usable (400).</summary>
public sealed class InvalidBatchMemberError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_batch_member";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
