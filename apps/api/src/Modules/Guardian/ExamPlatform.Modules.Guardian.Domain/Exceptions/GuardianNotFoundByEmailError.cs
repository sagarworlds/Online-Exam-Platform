using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>No guardian is registered with the e-mail address staff looked up.</summary>
public sealed class GuardianNotFoundByEmailError() : DomainException("No guardian is registered with that e-mail address.")
{
    public override string ErrorCode => "guardian_not_found";
    public override int HttpStatusCode => 404;
}
