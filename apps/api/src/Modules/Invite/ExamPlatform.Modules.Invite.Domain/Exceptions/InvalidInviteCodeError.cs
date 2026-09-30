using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Exceptions;

public sealed class InvalidInviteCodeError(string reason) : DomainException($"Invalid invite code: {reason}")
{
    public override string ErrorCode => "invalid_invite_code";
    public override int HttpStatusCode => 400;
}
