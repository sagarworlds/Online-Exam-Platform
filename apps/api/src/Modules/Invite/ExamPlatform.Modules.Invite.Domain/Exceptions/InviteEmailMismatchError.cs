using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>The signed-in account does not hold the e-mail address the invite was sent to.</summary>
public sealed class InviteEmailMismatchError()
    : DomainException("This invitation was sent to a different e-mail address. Sign in with the address it was sent to.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invite_email_mismatch";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
