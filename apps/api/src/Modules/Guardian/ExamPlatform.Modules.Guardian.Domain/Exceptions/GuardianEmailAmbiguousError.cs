using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

/// <summary>
/// More than one guardian is registered with the address, so linking a candidate to "the" guardian would pick one arbitrarily. The
/// refusal names the problem and stops there: merging the records is a decision for a person.
/// </summary>
public sealed class GuardianEmailAmbiguousError() : DomainException(
    "More than one guardian is registered with that e-mail address, so none can be chosen. The duplicate records need merging first.")
{
    public override string ErrorCode => "guardian_email_ambiguous";
    public override int HttpStatusCode => 409;
}
