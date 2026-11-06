using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>Direct sign-in links are switched off for this deployment (<c>Invite:DirectSignIn:Enabled</c>).</summary>
public sealed class DirectSignInDisabledError()
    : DomainException("Signing in from an invitation link is switched off. Sign in with a one-time code instead.")
{
    /// <inheritdoc />
    public override string ErrorCode => "direct_sign_in_disabled";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
