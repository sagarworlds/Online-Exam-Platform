using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>
/// A Super Admin account already exists, so there is no first administrator to provision. A deployed environment can be
/// given one only while it has none: anyone who can run the job could otherwise add an administrator at will.
/// </summary>
public sealed class FirstAdministratorAlreadyExistsError()
    : DomainException("A Super Admin account already exists, so no first administrator was created. "
        + "If its owner has lost the password, use 'Forgot password' on the sign-in page.")
{
    /// <inheritdoc />
    public override string ErrorCode => "first_administrator_exists";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
