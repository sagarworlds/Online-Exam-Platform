using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>No invitation was sent to the address whose invitations were to be changed. The address is not repeated in the message.</summary>
public sealed class NoInvitesForEmailError() : DomainException("No invitations were sent to that address.")
{
    /// <inheritdoc />
    public override string ErrorCode => "no_invites_for_email";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
