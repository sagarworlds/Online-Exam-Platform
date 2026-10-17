using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The e-mail address staff typed for an account is not a valid address.</summary>
public sealed class InvalidEmailAddressError() : DomainException("Enter a valid e-mail address.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_email_address";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
