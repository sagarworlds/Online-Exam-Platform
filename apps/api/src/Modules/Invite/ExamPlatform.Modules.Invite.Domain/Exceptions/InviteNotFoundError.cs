using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

public sealed class InviteNotFoundError(Guid inviteId) : DomainException($"Invite '{inviteId}' not found.")
{
    public override string ErrorCode => "invite_not_found";
    public override int HttpStatusCode => 404;
}
