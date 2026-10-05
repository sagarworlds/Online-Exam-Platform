using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

/// <summary>The batch has no active member with the given id (404).</summary>
public sealed class BatchMemberNotFoundError(Guid memberId) : DomainException($"Batch member '{memberId}' not found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "batch_member_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
