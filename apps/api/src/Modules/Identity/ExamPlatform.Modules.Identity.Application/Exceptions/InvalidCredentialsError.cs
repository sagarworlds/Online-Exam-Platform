using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The supplied password did not match the stored hash for this account.</summary>
public sealed class InvalidCredentialsError() : DomainException("Incorrect email/phone or password.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_credentials";
}
