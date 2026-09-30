using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

public sealed class GuardianNotFoundError(Guid guardianId) : DomainException($"Guardian '{guardianId}' not found.")
{
    public override string ErrorCode => "guardian_not_found";
    public override int HttpStatusCode => 404;
}
