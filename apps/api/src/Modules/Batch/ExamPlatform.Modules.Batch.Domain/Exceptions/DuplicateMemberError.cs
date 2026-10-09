using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

/// <summary>The e-mail address already holds a seat in the batch (409).</summary>
public sealed class DuplicateMemberError() : DomainException("A member with this e-mail address already exists in this batch.")
{
    // The address is left out of the message on purpose: the message is logged and returned to the caller (NFR-6).

    /// <inheritdoc />
    public override string ErrorCode => "duplicate_member";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
