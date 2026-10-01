using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>An email address or phone number is blank or longer than the platform stores.</summary>
public sealed class InvalidContactError() : DomainException("The email address or phone number is missing or too long.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_contact";
}
