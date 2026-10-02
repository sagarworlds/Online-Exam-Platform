using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>The address an invitation was to be sent to is not a valid e-mail address.</summary>
public sealed class InvalidInviteEmailError() : DomainException("The invitation needs a valid e-mail address.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_invite_email";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
