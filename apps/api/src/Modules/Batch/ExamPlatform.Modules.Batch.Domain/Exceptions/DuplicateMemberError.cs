using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

public sealed class DuplicateMemberError(string email) : DomainException($"Member with email '{email}' already exists in this batch.")
{
    public override string ErrorCode => "duplicate_member";
    public override int HttpStatusCode => 409;
}
