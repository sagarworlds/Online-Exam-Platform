using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Exceptions;

public sealed class InvalidLinkTokenError(string reason) : DomainException($"Invalid guardian link token: {reason}")
{
    public override string ErrorCode => "invalid_link_token";
    public override int HttpStatusCode => 400;
}
