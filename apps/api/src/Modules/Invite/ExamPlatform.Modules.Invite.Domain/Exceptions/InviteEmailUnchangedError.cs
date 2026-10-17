using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>The new address is the address the invitations already have, whatever its case, so there is nothing to change.</summary>
public sealed class InviteEmailUnchangedError() : DomainException("The new address is the same as the current one. Enter a different address.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invite_email_unchanged";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
