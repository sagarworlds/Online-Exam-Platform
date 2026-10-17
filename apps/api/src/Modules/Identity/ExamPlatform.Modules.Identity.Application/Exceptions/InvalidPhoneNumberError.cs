using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The mobile number staff typed for an account does not look like a phone number.</summary>
public sealed class InvalidPhoneNumberError() : DomainException(
    "Enter a valid mobile number: digits, with an optional + at the start. Spaces, dashes and brackets are allowed.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_phone_number";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
