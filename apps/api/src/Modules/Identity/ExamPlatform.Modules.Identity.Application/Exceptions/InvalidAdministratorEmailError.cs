using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The address given for the first administrator is not one plain e-mail address.</summary>
public sealed class InvalidAdministratorEmailError()
    : DomainException("The first administrator needs one plain e-mail address, such as name@example.org, with no display name around it.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_administrator_email";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
