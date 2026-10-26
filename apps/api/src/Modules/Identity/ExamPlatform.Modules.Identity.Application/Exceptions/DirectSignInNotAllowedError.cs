using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>
/// The account behind an invitation link cannot be signed in by the link: it is not a student account (staff need a password and a
/// code), or more than one account holds the address, so it is not clear whose it is.
/// </summary>
public sealed class DirectSignInNotAllowedError()
    : DomainException("This invitation link cannot sign you in. Sign in with your usual method instead.")
{
    /// <inheritdoc />
    public override string ErrorCode => "direct_sign_in_not_allowed";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
