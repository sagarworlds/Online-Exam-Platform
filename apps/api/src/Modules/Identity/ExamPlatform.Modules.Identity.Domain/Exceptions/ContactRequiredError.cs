using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>An account was registered with neither an email address nor a phone number.</summary>
public sealed class ContactRequiredError() : DomainException("An email address, a phone number, or both is required.")
{
    /// <inheritdoc />
    public override string ErrorCode => "contact_required";
}
