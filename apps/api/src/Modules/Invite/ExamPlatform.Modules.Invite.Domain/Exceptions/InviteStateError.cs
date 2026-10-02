using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

/// <summary>An invite action is not allowed in the invite's current state (already accepted, declined, revoked...).</summary>
/// <param name="message">What cannot be done and why, safe to show to the caller.</param>
public sealed class InviteStateError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invite_state_invalid";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
